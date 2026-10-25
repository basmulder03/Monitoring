namespace Plover.Agent;

/// <summary>
/// Whether this agent is the one that serves its scope. Only registered when several agents run for one scope; without it every
/// agent serves, which is what a single agent does.
/// </summary>
public interface ILeadership
{
    /// <summary>Whether this agent holds the lease right now.</summary>
    bool IsLeader { get; }

    /// <summary>Completes once this agent is the leader. A leader that loses the lease stops the agent, so this never has to be awaited again.</summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A task that completes when the lease is held.</returns>
    Task WaitUntilLeaderAsync(CancellationToken ct);
}
