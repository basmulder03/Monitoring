using System.Threading.Channels;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>Default <see cref="IScopeStateStore"/>: one lock around the instances, the pending changes and the subscribers.</summary>
/// <param name="options">The scope name and the subscriber buffer size.</param>
internal sealed class ScopeStateStore(ScopeStateOptions options) : IScopeStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, InstanceInfo> _platform = [];
    private readonly Dictionary<string, Application> _applications = [];

    /// <summary>The instances as shown: the platform's view combined with the application's report.</summary>
    private readonly Dictionary<string, InstanceInfo> _instances = [];
    private readonly Dictionary<string, InstanceInfo> _pendingUpserts = [];
    private readonly HashSet<string> _pendingRemoved = [];
    private readonly List<Subscriber> _subscribers = [];
    private long _sequence;

    /// <inheritdoc />
    public string ScopeName => options.ScopeName;

    /// <inheritdoc />
    public void Upsert(InstanceInfo instance)
    {
        lock (_gate)
        {
            _platform[instance.Id] = instance;
            Recompute(instance.Id);
        }
    }

    /// <inheritdoc />
    public void Remove(string id)
    {
        lock (_gate)
        {
            _platform.Remove(id);
            _applications.Remove(id);
            Recompute(id);
        }
    }

    /// <inheritdoc />
    public void Report(string id, ApplicationReport report)
    {
        lock (_gate)
        {
            _applications[id] = new Application(report, SidecarLost: false);
            Recompute(id);
        }
    }

    /// <inheritdoc />
    public InstanceInfo? Find(string id)
    {
        lock (_gate)
        {
            return _platform.GetValueOrDefault(id);
        }
    }

    /// <inheritdoc />
    public void LoseSidecar(string id)
    {
        lock (_gate)
        {
            if (_applications.TryGetValue(id, out var application))
            {
                _applications[id] = application with { SidecarLost = true };
                Recompute(id);
            }
        }
    }

    /// <inheritdoc />
    public void ForgetReport(string id)
    {
        lock (_gate)
        {
            if (_applications.Remove(id))
            {
                Recompute(id);
            }
        }
    }

    /// <summary>Works out the instance to show and records the change, if what is shown changed. The caller holds the lock.</summary>
    private void Recompute(string id)
    {
        if (!_platform.TryGetValue(id, out var platform))
        {
            if (_instances.Remove(id))
            {
                // An instance added and removed within one batch is still reported as removed: harmless if the
                // controller never saw it, and required if an earlier snapshot already contained it.
                _pendingUpserts.Remove(id);
                _pendingRemoved.Add(id);
            }

            return;
        }

        _applications.TryGetValue(id, out var application);
        var shown = InstanceMerger.Merge(platform, application?.Report, application?.SidecarLost ?? false);

        // Heartbeats and re-lists restate what is already known; only a real change is worth telling the controllers.
        if (_instances.TryGetValue(id, out var current) && InstanceEquality.AreEqual(current, shown))
        {
            return;
        }

        _instances[id] = shown;
        _pendingUpserts[id] = shown;
        _pendingRemoved.Remove(id);
    }

    /// <inheritdoc />
    public void Publish()
    {
        lock (_gate)
        {
            if (_pendingUpserts.Count == 0 && _pendingRemoved.Count == 0)
            {
                return;
            }

            _sequence++;
            var delta = new Delta(
                _sequence,
                [.. _pendingUpserts.Values.OrderBy(i => i.Id, StringComparer.Ordinal)],
                [.. _pendingRemoved.Order(StringComparer.Ordinal)]);
            _pendingUpserts.Clear();
            _pendingRemoved.Clear();

            // Never wait on a slow controller while holding the lock: cut it off instead, and let it resync.
            foreach (var subscriber in _subscribers.ToList())
            {
                if (!subscriber.Channel.Writer.TryWrite(delta))
                {
                    subscriber.Channel.Writer.TryComplete(new InvalidOperationException(
                        $"The controller connection fell more than {options.SubscriberBuffer} deltas behind."));
                    _subscribers.Remove(subscriber);
                }
            }
        }
    }

    /// <inheritdoc />
    public Snapshot CreateSnapshot()
    {
        lock (_gate)
        {
            return SnapshotLocked();
        }
    }

    /// <inheritdoc />
    public StateSubscription Subscribe()
    {
        lock (_gate)
        {
            var channel = Channel.CreateBounded<Delta>(new BoundedChannelOptions(options.SubscriberBuffer)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
            var subscriber = new Subscriber(channel);
            _subscribers.Add(subscriber);
            return new StateSubscription(SnapshotLocked(), channel.Reader, () => Unsubscribe(subscriber));
        }
    }

    private Snapshot SnapshotLocked() =>
        new(
            _sequence,
            options.ScopeName,
            [.. _instances.Values.OrderBy(i => i.Id, StringComparer.Ordinal)],
            new Dictionary<string, string>(options.Labels));

    private void Unsubscribe(Subscriber subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }

        subscriber.Channel.Writer.TryComplete();
    }

    /// <summary>What an application reported, and whether its sidecar has since gone away.</summary>
    private sealed record Application(ApplicationReport Report, bool SidecarLost);

    /// <summary>One controller connection's delta queue.</summary>
    private sealed record Subscriber(Channel<Delta> Channel);
}
