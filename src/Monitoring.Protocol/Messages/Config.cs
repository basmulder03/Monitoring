namespace Monitoring.Protocol.Messages;

/// <summary>Effective configuration plus every controller replica address, sent on connect and on every change.</summary>
/// <param name="Version">Hash of the configuration; agents report the version they applied so drift is visible.</param>
/// <param name="ControllerReplicas">Individual address of every controller replica; the agent connects to all of them.</param>
/// <param name="HeartbeatSeconds">Interval between <see cref="Heartbeat"/> messages.</param>
/// <param name="PullSeconds">Interval between metric pulls.</param>
/// <param name="EdgeFlushSeconds">Interval between flushes of dependency edge counters.</param>
public sealed record Config(
    string Version,
    IReadOnlyList<string> ControllerReplicas,
    int HeartbeatSeconds = 5,
    int PullSeconds = 15,
    int EdgeFlushSeconds = 10) : Message;
