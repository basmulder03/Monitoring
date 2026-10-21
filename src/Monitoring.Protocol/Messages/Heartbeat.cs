using Monitoring.Protocol.Health;

namespace Monitoring.Protocol.Messages;

/// <summary>Liveness signal, sent every 5 s by default.</summary>
/// <param name="Sequence">Increases by one per heartbeat, so the receiver can detect gaps.</param>
/// <param name="Health">One-byte health summary of the sender.</param>
/// <param name="ConfigVersion">The version of the configuration the sender uses, so the controller can tell whether a change has reached it; <see langword="null"/> for senders that have none.</param>
public sealed record Heartbeat(long Sequence, HealthState Health, string? ConfigVersion = null) : Message;
