namespace Plover.Controller.Features.Topology;

/// <summary>The current dependency graph.</summary>
public interface ITopology
{
    /// <summary>Gets the graph as of the last sample. Edges with no calls for a while are no longer part of it.</summary>
    /// <returns>The graph; safe to keep, it never changes.</returns>
    TopologyGraph GetGraph();
}
