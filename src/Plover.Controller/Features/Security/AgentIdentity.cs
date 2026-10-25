namespace Plover.Controller.Features.Security;

/// <summary>Who an authenticated agent is, and therefore which scope it may report as.</summary>
/// <param name="ScopeName">The only scope the agent may send state for.</param>
public sealed record AgentIdentity(string ScopeName);
