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

    /// <summary>
    /// Adds an instance as the platform sees it, or replaces the one with the same id. If the application has reported about itself,
    /// the shown instance combines both. Takes effect for controllers at the next <see cref="Publish"/>, and only when it changes what is shown.
    /// </summary>
    /// <param name="instance">The instance as it is now.</param>
    void Upsert(InstanceInfo instance);

    /// <summary>Removes an instance and what its application reported, which controllers then show as Gone. Unknown ids are ignored.</summary>
    /// <param name="id">Id of the instance that no longer exists.</param>
    void Remove(string id);

    /// <summary>
    /// Records what the application of an instance says about itself. It adds detail to an instance the platform knows about;
    /// it never makes an instance exist, so a report for an unknown instance is kept until the platform reports it.
    /// </summary>
    /// <param name="id">Id of the instance, which is the pod name.</param>
    /// <param name="report">What the application reports.</param>
    void Report(string id, ApplicationReport report);

    /// <summary>
    /// Records that the sidecar of an instance is no longer connected. The instance is shown as Unhealthy until it reports again,
    /// because nothing can be said about its application.
    /// </summary>
    /// <param name="id">Id of the instance.</param>
    void LoseSidecar(string id);

    /// <summary>Looks up an instance as the platform (the Kubernetes watch or the Service Control Manager) reports it, without what applications say about themselves.</summary>
    /// <param name="id">The instance id.</param>
    /// <returns>The instance, or <see langword="null"/> when the platform does not know it (yet).</returns>
    InstanceInfo? Find(string id);

    /// <summary>Forgets what the application of an instance reported, for example when its sidecar said goodbye on purpose.</summary>
    /// <param name="id">Id of the instance.</param>
    void ForgetReport(string id);

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
