namespace Monitoring.Common.Prometheus;

/// <summary>One value to expose.</summary>
/// <param name="Name">The metric name; characters Prometheus does not allow in a name, such as dots, are replaced when written.</param>
/// <param name="Labels">The labels of the value.</param>
/// <param name="Value">The value.</param>
public sealed record PrometheusSample(string Name, IReadOnlyDictionary<string, string> Labels, double Value);
