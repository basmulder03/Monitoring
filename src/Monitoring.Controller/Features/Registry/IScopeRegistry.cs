using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Registry;

/// <summary>
/// In-memory registry of the instances in every scope, rebuilt entirely from what agents send.
/// </summary>
/// <remarks>All members are safe to call from several threads.</remarks>
public interface IScopeRegistry
{
    /// <summary>Replaces the whole state of a scope with the snapshot.</summary>
    /// <param name="snapshot">Full state of the scope; always accepted, because an agent restart resets its sequence.</param>
    /// <returns>The outcome and the sequence number to acknowledge.</returns>
    ApplyResult ApplySnapshot(Snapshot snapshot);

    /// <summary>Applies the changes since the previous message of a scope.</summary>
    /// <param name="scopeName">The scope of the authenticated agent connection that sent the delta.</param>
    /// <param name="delta">The changes to apply.</param>
    /// <returns>The outcome and the sequence number to acknowledge.</returns>
    ApplyResult ApplyDelta(string scopeName, Delta delta);

    /// <summary>Gets the instances of a scope, including those shown as Gone, ordered by id.</summary>
    /// <param name="scopeName">The scope to read.</param>
    /// <returns>The instances, or an empty list for an unknown scope.</returns>
    IReadOnlyList<InstanceInfo> GetInstances(string scopeName);

    /// <summary>Gets the names of all scopes that have sent a snapshot.</summary>
    /// <returns>The scope names, ordered.</returns>
    IReadOnlyList<string> GetScopeNames();

    /// <summary>Drops instances that have been Gone for longer than <see cref="RegistryOptions.GoneRetention"/>.</summary>
    void PurgeExpired();
}
