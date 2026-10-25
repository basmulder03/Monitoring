namespace Plover.Sidecar.Features.AgentLink;

/// <summary>How a session with the agent ended.</summary>
/// <param name="End">The reason it ended.</param>
/// <param name="Established">Whether the agent accepted the registration, that is whether it answered with its configuration.</param>
internal sealed record SessionResult(SessionEnd End, bool Established);
