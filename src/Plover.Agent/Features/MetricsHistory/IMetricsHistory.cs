using Plover.Protocol.Reports;

namespace Plover.Agent.Features.MetricsHistory;

/// <summary>A small, bounded memory of the recent values of the metrics of each instance, for the sparklines a controller shows.</summary>
/// <remarks>All members are safe to call from several threads.</remarks>
public interface IMetricsHistory
{
    /// <summary>Adds the latest value of every series of an instance. Series beyond the limit per instance are not kept.</summary>
    /// <param name="instanceId">The instance, which is the pod name.</param>
    /// <param name="series">The latest values.</param>
    void Record(string instanceId, IReadOnlyList<MetricSeries> series);

    /// <summary>Drops everything kept for an instance, for example when its sidecar has gone.</summary>
    /// <param name="instanceId">The instance.</param>
    void Forget(string instanceId);

    /// <summary>Gets what is kept for an instance.</summary>
    /// <param name="instanceId">The instance.</param>
    /// <param name="intervalSeconds">The time between two recorded values, which the history cannot know itself because the pull interval is configured centrally.</param>
    /// <returns>The history, or <see langword="null"/> when nothing is kept for the instance.</returns>
    MetricHistory? Get(string instanceId, int intervalSeconds);
}
