using System.Threading.Channels;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>Default <see cref="IScopeStateStore"/>: one lock around the instances, the pending changes and the subscribers.</summary>
/// <param name="options">The scope name and the subscriber buffer size.</param>
internal sealed class ScopeStateStore(ScopeStateOptions options) : IScopeStateStore
{
    private readonly Lock _gate = new();
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
            _instances[instance.Id] = instance;
            _pendingUpserts[instance.Id] = instance;
            _pendingRemoved.Remove(instance.Id);
        }
    }

    /// <inheritdoc />
    public void Remove(string id)
    {
        lock (_gate)
        {
            if (!_instances.Remove(id))
            {
                return;
            }

            // An instance added and removed within one batch is still reported as removed: harmless if the
            // controller never saw it, and required if an earlier snapshot already contained it.
            _pendingUpserts.Remove(id);
            _pendingRemoved.Add(id);
        }
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

    /// <summary>One controller connection's delta queue.</summary>
    private sealed record Subscriber(Channel<Delta> Channel);
}
