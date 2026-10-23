using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Counts the calls to each dependency: how many, how many failed and how long they took. Never sampled.</summary>
/// <remarks>Memory is bounded by <paramref name="maxEdges"/>; calls to further dependencies are not counted. Safe to use from any thread.</remarks>
/// <param name="maxEdges">The most dependencies kept.</param>
internal sealed class EdgeCounters(int maxEdges)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Type, string Target), Counter> _edges = [];

    /// <summary>How many dependencies are counted now; at the limit, calls to further ones are not counted.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _edges.Count;
            }
        }
    }

    /// <summary>Counts one finished call.</summary>
    /// <param name="type">Kind of dependency.</param>
    /// <param name="target">Which dependency.</param>
    /// <param name="durationMs">How long the call took, in milliseconds.</param>
    /// <param name="error">Whether it failed.</param>
    public void Record(string type, string target, double durationMs, bool error)
    {
        lock (_gate)
        {
            if (!_edges.TryGetValue((type, target), out var counter))
            {
                if (_edges.Count >= maxEdges)
                {
                    return;
                }

                _edges[(type, target)] = counter = new Counter();
            }

            counter.Calls++;
            counter.Errors += error ? 1 : 0;
            counter.DurationSumMs += durationMs;
            counter.Buckets[BucketOf(durationMs)]++;
        }
    }

    /// <summary>Reads the counters.</summary>
    /// <returns>One entry per dependency, ordered by kind and target so the answer is stable.</returns>
    public EdgesReport Read()
    {
        lock (_gate)
        {
            return new EdgesReport([.. _edges
                .OrderBy(pair => pair.Key.Type, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Target, StringComparer.Ordinal)
                .Select(pair => new EdgeReport(pair.Key.Type, pair.Key.Target, pair.Value.Calls, pair.Value.Errors, pair.Value.DurationSumMs, [.. pair.Value.Buckets]))]);
        }
    }

    private static int BucketOf(double durationMs)
    {
        var bounds = EdgesReport.BucketBoundsMs;
        for (var i = 0; i < bounds.Count; i++)
        {
            if (durationMs <= bounds[i])
            {
                return i;
            }
        }

        return bounds.Count;
    }

    private sealed class Counter
    {
        public long Calls { get; set; }

        public long Errors { get; set; }

        public double DurationSumMs { get; set; }

        public long[] Buckets { get; } = new long[EdgesReport.BucketBoundsMs.Count + 1];
    }
}
