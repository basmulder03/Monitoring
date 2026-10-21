namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>How a session with a controller replica ended.</summary>
/// <param name="End">The reason it ended.</param>
/// <param name="Established">Whether the controller accepted the agent, that is whether it answered with its configuration.</param>
internal sealed record SessionResult(SessionEnd End, bool Established);
