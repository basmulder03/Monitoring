namespace Plover.Controller.Features.Liveness;

/// <summary>Settings of the liveness slice.</summary>
public sealed class LivenessOptions
{
    /// <summary>How often agents are told to send a heartbeat.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many heartbeats in a row may be missing before the agent counts as gone.</summary>
    public int MissedHeartbeats { get; set; } = 3;

    /// <summary>How long an agent may stay silent before it counts as gone: the missed heartbeats together.</summary>
    public TimeSpan Timeout => HeartbeatInterval * MissedHeartbeats;
}
