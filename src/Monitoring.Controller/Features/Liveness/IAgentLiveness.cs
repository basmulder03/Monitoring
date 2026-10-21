namespace Monitoring.Controller.Features.Liveness;

/// <summary>
/// Tracks which scopes have an agent that is alive. An agent is alive while it is connected and has been heard from
/// within the heartbeat timeout; a closed connection counts as gone at once.
/// </summary>
/// <remarks>
/// Each controller replica judges for itself from its own connections, so replicas can briefly disagree.
/// All members are safe to call from several threads.
/// </remarks>
public interface IAgentLiveness
{
    /// <summary>Records that an agent connected for a scope.</summary>
    /// <param name="scopeName">The scope the agent reports for.</param>
    /// <param name="connectionId">Identifies the connection, because a scope can briefly have two when an agent reconnects.</param>
    void Connected(string scopeName, Guid connectionId);

    /// <summary>Records that something, a heartbeat or any other message, arrived on a connection.</summary>
    /// <param name="scopeName">The scope the agent reports for.</param>
    /// <param name="connectionId">The connection it arrived on.</param>
    void Seen(string scopeName, Guid connectionId);

    /// <summary>Records that a connection ended.</summary>
    /// <param name="scopeName">The scope the agent reported for.</param>
    /// <param name="connectionId">The connection that ended.</param>
    void Disconnected(string scopeName, Guid connectionId);

    /// <summary>Tells whether the agent of a scope is alive.</summary>
    /// <param name="scopeName">The scope to check.</param>
    /// <returns>The liveness; a scope that no agent ever connected for is not live.</returns>
    ScopeLiveness GetScopeLiveness(string scopeName);
}
