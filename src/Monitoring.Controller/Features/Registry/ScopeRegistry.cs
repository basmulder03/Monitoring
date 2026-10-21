using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Registry;

/// <summary>
/// Default <see cref="IScopeRegistry"/>: one dictionary of scopes, guarded by a single lock.
/// </summary>
/// <remarks>
/// A single lock is enough because every operation is a short in-memory update and the controller applies messages
/// from a bounded number of agent connections. Time comes from <paramref name="time"/> so retention is testable.
/// </remarks>
/// <param name="options">Retention settings.</param>
/// <param name="time">Clock used to stamp and expire Gone instances.</param>
internal sealed class ScopeRegistry(RegistryOptions options, TimeProvider time) : IScopeRegistry
{
    private readonly Lock _gate = new();

    /// <summary>State per scope name; guarded by <see cref="_gate"/>.</summary>
    private readonly Dictionary<string, ScopeState> _scopes = [];

    /// <inheritdoc />
    public ApplyResult ApplySnapshot(Snapshot snapshot)
    {
        lock (_gate)
        {
            // A new state object also drops instances the agent no longer reports, including expired Gone ones.
            var scope = new ScopeState { LastSequence = snapshot.Sequence };
            foreach (var instance in snapshot.Instances)
            {
                scope.Instances[instance.Id] = Track(instance);
            }

            _scopes[snapshot.ScopeName] = scope;
            return new ApplyResult(ApplyOutcome.Applied, scope.LastSequence);
        }
    }

    /// <inheritdoc />
    public ApplyResult ApplyDelta(string scopeName, Delta delta)
    {
        lock (_gate)
        {
            if (!_scopes.TryGetValue(scopeName, out var scope))
            {
                return new ApplyResult(ApplyOutcome.SnapshotRequired, null);
            }

            // At or below the last applied sequence means the delta was already seen, for example after a reconnect.
            if (delta.Sequence <= scope.LastSequence)
            {
                return new ApplyResult(ApplyOutcome.Duplicate, scope.LastSequence);
            }

            // A skipped sequence is still applied: the data is newer, and the next snapshot repairs anything missed.
            var outcome = delta.Sequence == scope.LastSequence + 1 ? ApplyOutcome.Applied : ApplyOutcome.AppliedWithGap;

            foreach (var instance in delta.Upserted)
            {
                scope.Instances[instance.Id] = Track(instance);
            }

            foreach (var id in delta.Removed)
            {
                // Keep the instance as Gone, so the UI can show what disappeared; removal of an unknown or already Gone id is a no-op.
                if (scope.Instances.TryGetValue(id, out var existing) && existing.GoneSince is null)
                {
                    scope.Instances[id] = Track(existing.Info with { Lifecycle = InstanceLifecycle.Gone });
                }
            }

            scope.LastSequence = delta.Sequence;
            return new ApplyResult(outcome, scope.LastSequence);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<InstanceInfo> GetInstances(string scopeName)
    {
        lock (_gate)
        {
            return _scopes.TryGetValue(scopeName, out var scope)
                ? [.. scope.Instances.Values.Select(t => t.Info).OrderBy(i => i.Id, StringComparer.Ordinal)]
                : [];
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetScopeNames()
    {
        lock (_gate)
        {
            return [.. _scopes.Keys.OrderBy(name => name, StringComparer.Ordinal)];
        }
    }

    /// <inheritdoc />
    public void PurgeExpired()
    {
        lock (_gate)
        {
            var cutoff = time.GetUtcNow() - options.GoneRetention;
            foreach (var scope in _scopes.Values)
            {
                var expired = scope.Instances
                    .Where(pair => pair.Value.GoneSince <= cutoff)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var id in expired)
                {
                    scope.Instances.Remove(id);
                }
            }
        }
    }

    /// <summary>Wraps an instance for storage, stamping the time it became Gone when it already is.</summary>
    private ScopeState.TrackedInstance Track(InstanceInfo info) =>
        new(info, info.Lifecycle == InstanceLifecycle.Gone ? time.GetUtcNow() : null);
}
