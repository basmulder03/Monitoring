using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>
/// The agent's in-memory record of the instances in its scope, and the source of the snapshots and deltas
/// sent to every controller replica.
/// </summary>
/// <remarks>
/// Changes are collected and sent as one delta by <see cref="Publish"/>. Sequence numbers belong to the scope, not
/// to a connection, so every replica receives the same deltas with the same numbers. A snapshot carries the current
/// sequence number without consuming one. All members are safe to call from several threads.
/// </remarks>
public interface IScopeStateStore
{
    /// <summary>The scope this agent reports as.</summary>
    string ScopeName { get; }

    /// <summary>Adds an instance or replaces the instance with the same id. Takes effect for controllers at the next <see cref="Publish"/>.</summary>
    /// <param name="instance">The instance as it is now.</param>
    void Upsert(InstanceInfo instance);

    /// <summary>Removes an instance, which controllers then show as Gone. Unknown ids are ignored.</summary>
    /// <param name="id">Id of the instance that no longer exists.</param>
    void Remove(string id);

    /// <summary>Sends everything changed since the previous call as one delta to every subscriber; does nothing when nothing changed.</summary>
    void Publish();

    /// <summary>Gets the full state at the current sequence number, for example for periodic reconciliation.</summary>
    /// <returns>The snapshot.</returns>
    Snapshot CreateSnapshot();

    /// <summary>
    /// Starts receiving deltas, together with the snapshot they continue from. Both are taken at the same moment,
    /// so nothing is missed or sent twice between them.
    /// </summary>
    /// <returns>The subscription; dispose it when the controller connection ends.</returns>
    StateSubscription Subscribe();
}
