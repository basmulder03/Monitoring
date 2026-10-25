namespace Plover.Protocol.Reports;

/// <summary>The latest value of one metric of an application.</summary>
/// <param name="Name">Name of the instrument, such as <c>http.server.request.duration.sum</c>.</param>
/// <param name="Labels">The tags of the measurement; empty when it has none.</param>
/// <param name="Value">The value now: a running total for counters, the last reading for gauges.</param>
public sealed record MetricSeries(string Name, IReadOnlyDictionary<string, string> Labels, double Value);
