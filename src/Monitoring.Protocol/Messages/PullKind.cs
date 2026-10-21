namespace Monitoring.Protocol.Messages;

/// <summary>The kind of data a <see cref="Pull"/> asks for.</summary>
public enum PullKind : byte
{
    /// <summary>Static instance information.</summary>
    Info,

    /// <summary>Results of the instance's health checks.</summary>
    Health,

    /// <summary>Metric values.</summary>
    Metrics,

    /// <summary>Recent spans, fetched only for drill-down.</summary>
    Spans,

    /// <summary>Aggregated caller-to-callee edge counters.</summary>
    Edges,
}
