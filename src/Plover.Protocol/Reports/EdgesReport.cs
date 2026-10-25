namespace Plover.Protocol.Reports;

/// <summary>The dependency edges of an application; the answer to a pull for <see cref="Messages.PullKind.Edges"/>.</summary>
/// <param name="Edges">One entry per dependency. Counters only grow, so a missed pull loses nothing.</param>
public sealed record EdgesReport(IReadOnlyList<EdgeReport> Edges)
{
    /// <summary>Upper bounds, in milliseconds, of the latency buckets in <see cref="EdgeReport.Buckets"/>.</summary>
    public static IReadOnlyList<double> BucketBoundsMs { get; } = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000];
}
