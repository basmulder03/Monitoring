using Monitoring.Protocol.Instances;

namespace Monitoring.Protocol.Messages;

/// <summary>Only what changed since the previous message of the same agent.</summary>
/// <param name="Sequence">Position in the agent's message sequence; replicas acknowledge the last one applied.</param>
/// <param name="Upserted">Instances that were added or changed.</param>
/// <param name="Removed">Ids of instances that reached <see cref="InstanceLifecycle.Gone"/>.</param>
public sealed record Delta(
    long Sequence,
    IReadOnlyList<InstanceInfo> Upserted,
    IReadOnlyList<string> Removed) : Message;
