namespace Plover.Protocol.Messages;

/// <summary>Identity, sent first by the child on every new connection.</summary>
/// <param name="ProtocolVersion">Protocol version of the sender; adjacent versions must interoperate during rollouts.</param>
/// <param name="InstanceId">Stable id of the sending instance (pod, process or agent).</param>
/// <param name="ServiceName">Logical service name the instance belongs to.</param>
/// <param name="Version">Application version, or <see langword="null"/> when unknown.</param>
/// <param name="Labels">Free-form labels such as <c>environment</c>, <c>datacenter</c> and <c>namespace</c>.</param>
/// <param name="Token">Authentication token; <see langword="null"/> when another authenticator is used.</param>
/// <param name="ScopeName">
/// The scope the sender reports as, so an agent can register itself without the controller being told about it in advance.
/// <see langword="null"/> for senders that are not agents, or older ones; the controller then uses the scope its token is bound to.
/// </param>
public sealed record Hello(
    int ProtocolVersion,
    string InstanceId,
    string ServiceName,
    string? Version,
    IReadOnlyDictionary<string, string> Labels,
    string? Token = null,
    string? ScopeName = null) : Message;
