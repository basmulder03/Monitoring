namespace Plover.Sidecar.Features.AgentLink;

/// <summary>Why a session with the agent ended.</summary>
internal enum SessionEnd : byte
{
    /// <summary>The agent did not answer the registration with its configuration.</summary>
    Refused,

    /// <summary>The connection broke or the agent closed it.</summary>
    Disconnected,

    /// <summary>The application introduced itself differently, for example with a new version, so it must register again.</summary>
    Reregister,

    /// <summary>The sidecar is shutting down.</summary>
    Stopped,
}
