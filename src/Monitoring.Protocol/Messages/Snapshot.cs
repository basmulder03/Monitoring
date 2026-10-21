using Monitoring.Protocol.Instances;

namespace Monitoring.Protocol.Messages;

/// <summary>Full state of an agent's scope, sent on connect and every 5 minutes as reconciliation.</summary>
/// <param name="Sequence">Position in the agent's message sequence; later <see cref="Delta"/> messages continue from it.</param>
/// <param name="ScopeName">Display name of the scope (a namespace or a server).</param>
/// <param name="Instances">Every instance currently known in the scope.</param>
public sealed record Snapshot(long Sequence, string ScopeName, IReadOnlyList<InstanceInfo> Instances) : Message;
