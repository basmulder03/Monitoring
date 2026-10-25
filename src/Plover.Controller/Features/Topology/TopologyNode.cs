using Plover.Protocol.Health;

namespace Plover.Controller.Features.Topology;

/// <summary>One thing in the dependency graph.</summary>
/// <param name="Id">Stable identity, such as <c>service:shop/orders</c>, used by the edges.</param>
/// <param name="Name">What to show: the service name, or the address of a database, queue or external system.</param>
/// <param name="Kind">What the node stands for.</param>
/// <param name="Scope">The scope the service belongs to; <see langword="null"/> for nodes that are not services.</param>
/// <param name="Health">The worst health of the live instances of a service; <see langword="null"/> for nodes that are not services.</param>
public sealed record TopologyNode(string Id, string Name, NodeKind Kind, string? Scope, HealthState? Health);
