namespace Plover.Controller.Features.Topology;

/// <summary>Which services call what, built from what the applications report about their own calls.</summary>
/// <param name="Nodes">Every node that has an edge, ordered by id.</param>
/// <param name="Edges">Every edge that was active recently, ordered by caller and callee.</param>
public sealed record TopologyGraph(IReadOnlyList<TopologyNode> Nodes, IReadOnlyList<TopologyEdge> Edges)
{
    /// <summary>A graph with nothing in it.</summary>
    public static TopologyGraph Empty { get; } = new([], []);
}
