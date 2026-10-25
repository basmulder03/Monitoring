namespace Plover.Controller.Features.Topology;

/// <summary>Settings of the topology slice.</summary>
public sealed class TopologyOptions
{
    /// <summary>How often the registry is read to update the graph.</summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long an edge may go without a new call before it disappears from the graph.</summary>
    public TimeSpan EdgeFadeAfter { get; set; } = TimeSpan.FromMinutes(5);
}
