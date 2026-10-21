namespace Monitoring.Protocol.Messages;

/// <summary>
/// Sent by a controller replica to an agent after applying a <see cref="Snapshot"/> or <see cref="Delta"/>.
/// Each replica acknowledges independently, so an agent can tell which replica is behind.
/// </summary>
/// <param name="Sequence">The last sequence number the replica applied for the agent; 0 when it has applied nothing yet.</param>
/// <param name="SnapshotRequired">
/// <see langword="true"/> when the replica cannot use deltas yet, for example after a restart, and needs a new <see cref="Snapshot"/>.
/// </param>
public sealed record Ack(long Sequence, bool SnapshotRequired = false) : Message;
