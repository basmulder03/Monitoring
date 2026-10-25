namespace Plover.Protocol.Reports;

/// <summary>The names of the metrics in which the agent reports its state towards each controller replica, so every replica can see all of them.</summary>
public static class ReplicaMetrics
{
    /// <summary>Whether the agent is connected to a replica, 1 or 0; tagged with the <c>replica</c>.</summary>
    public const string Connected = "plover.agent.replica_connected";

    /// <summary>The last sequence a replica acknowledged; measured by the link, and not reported itself.</summary>
    public const string AckedSequence = "plover.agent.replica_acked_sequence";

    /// <summary>How many changes of the scope a replica has not acknowledged; tagged with the <c>replica</c>, and only for a replica the agent is connected to.</summary>
    public const string Lag = "plover.agent.replica_lag";

    /// <summary>The service the agent is shown as, in the pod it runs in.</summary>
    public const string AgentService = "plover-agent";

    /// <summary>Reads what an agent reported about its replicas out of the metrics of its instance.</summary>
    /// <param name="metrics">The metrics of the instance; <see langword="null"/> for none.</param>
    /// <returns>One entry per replica the agent connects to, ordered by address.</returns>
    public static IReadOnlyList<ReplicaReport> From(IReadOnlyList<MetricSeries>? metrics)
    {
        if (metrics is null)
        {
            return [];
        }

        return
        [
            .. metrics.Where(series => series.Name == Connected && series.Labels.ContainsKey("replica"))
                .OrderBy(series => series.Labels["replica"], StringComparer.Ordinal)
                .Select(series =>
                {
                    var replica = series.Labels["replica"];
                    var lag = metrics.FirstOrDefault(other => other.Name == Lag && other.Labels.GetValueOrDefault("replica") == replica);
                    return new ReplicaReport(replica, series.Value > 0, lag is null ? null : (long)lag.Value);
                }),
        ];
    }
}

/// <summary>What an agent says about one controller replica.</summary>
/// <param name="Replica">The replica as <c>host:port</c>.</param>
/// <param name="Connected">Whether the agent is connected to it.</param>
/// <param name="Lag">How many changes of its scope the replica has not acknowledged; <see langword="null"/> while the agent is not connected.</param>
public sealed record ReplicaReport(string Replica, bool Connected, long? Lag);
