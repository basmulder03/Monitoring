namespace Monitoring.Agent.Features.MetricsHistory;

/// <summary>Settings of the metrics history slice.</summary>
public sealed class MetricsHistoryOptions
{
    /// <summary>Gets or sets how many values are kept per series; at the default pull interval of 15 seconds, 60 values are 15 minutes.</summary>
    public int Samples { get; set; } = 60;

    /// <summary>Gets or sets how many series are kept per instance, which bounds the memory of an application that makes up new series without end.</summary>
    public int MaxSeriesPerInstance { get; set; } = 200;
}
