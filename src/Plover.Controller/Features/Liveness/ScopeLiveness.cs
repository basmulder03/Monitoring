namespace Plover.Controller.Features.Liveness;

/// <summary>Whether the agent of a scope is alive, as this controller replica sees it.</summary>
/// <param name="IsLive">Whether at least one connection of the scope's agent has been heard from recently.</param>
/// <param name="LastSeen">
/// When the agent was last heard from; <see langword="null"/> if it never was.
/// While the agent is live this keeps changing, so readers that need a stable value should only use it when not live.
/// </param>
public sealed record ScopeLiveness(bool IsLive, DateTimeOffset? LastSeen);
