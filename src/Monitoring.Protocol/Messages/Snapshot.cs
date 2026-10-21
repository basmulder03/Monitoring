using Monitoring.Protocol.Instances;

namespace Monitoring.Protocol.Messages;

/// <summary>Full state of an agent's scope, sent on connect and every 5 minutes as reconciliation.</summary>
/// <param name="Sequence">Position in the agent's message sequence; later <see cref="Delta"/> messages continue from it.</param>
/// <param name="ScopeName">Display name of the scope (a namespace or a server).</param>
/// <param name="Instances">Every instance currently known in the scope.</param>
/// <param name="Labels">
/// Free-form labels of the scope itself, such as <c>environment</c> and <c>datacenter</c>, which the dashboard groups by.
/// Sent with every snapshot, so a change reaches the controller at the next reconciliation. <see langword="null"/> for senders that have none.
/// </param>
public sealed record Snapshot(
    long Sequence,
    string ScopeName,
    IReadOnlyList<InstanceInfo> Instances,
    IReadOnlyDictionary<string, string>? Labels = null) : Message;
