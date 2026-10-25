namespace Plover.Agent.Features.ControllerLink;

/// <summary>Why a session with a controller replica ended.</summary>
internal enum SessionEnd : byte
{
    /// <summary>The controller refused the agent or did not answer the hello with its configuration.</summary>
    Refused,

    /// <summary>The connection broke or the controller closed it.</summary>
    Disconnected,

    /// <summary>The replica needs a fresh snapshot, or fell too far behind; reconnecting starts one.</summary>
    ResyncRequested,

    /// <summary>The agent is shutting down.</summary>
    Stopped,
}
