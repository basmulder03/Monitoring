namespace Monitoring.Protocol.Reports;

/// <summary>The latest value of every metric of an application; the answer to a pull for <see cref="Messages.PullKind.Metrics"/>.</summary>
/// <param name="Series">One entry per metric and combination of tags. A histogram is reported as a <c>.count</c> and a <c>.sum</c> series.</param>
public sealed record MetricsReport(IReadOnlyList<MetricSeries> Series);
