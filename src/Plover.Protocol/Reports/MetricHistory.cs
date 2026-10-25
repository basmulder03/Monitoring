namespace Plover.Protocol.Reports;

/// <summary>The recent values of the metrics of one instance, oldest first; the payload of a <see cref="Messages.HistoryResult"/>.</summary>
/// <param name="IntervalSeconds">The time between two values.</param>
/// <param name="Series">One entry per metric and combination of tags that has been seen.</param>
public sealed record MetricHistory(int IntervalSeconds, IReadOnlyList<SeriesHistory> Series);

/// <summary>The recent values of one metric.</summary>
/// <param name="Name">The metric name.</param>
/// <param name="Labels">The tags of the series.</param>
/// <param name="Values">The values, oldest first, at most as many as the agent keeps.</param>
public sealed record SeriesHistory(string Name, IReadOnlyDictionary<string, string> Labels, IReadOnlyList<double> Values);
