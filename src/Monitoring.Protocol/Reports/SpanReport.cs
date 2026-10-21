namespace Monitoring.Protocol.Reports;

/// <summary>One operation of an application, kept briefly so someone can look at slow or failed ones.</summary>
/// <param name="Sequence">Increases with every span the application records; pass the highest one seen as the cursor of the next pull.</param>
/// <param name="TraceId">W3C trace id, shared by all the operations of one request.</param>
/// <param name="SpanId">W3C span id.</param>
/// <param name="ParentId">Span id of the caller; <see langword="null"/> for the first operation of a trace.</param>
/// <param name="Name">Name of the operation.</param>
/// <param name="Kind">Client, Server, Producer or Consumer.</param>
/// <param name="StartUtc">When the operation began.</param>
/// <param name="DurationMs">How long it took, in milliseconds.</param>
/// <param name="Error">Whether it failed.</param>
/// <param name="Details">A few well-known facts about it, with query strings and literals removed.</param>
public sealed record SpanReport(
    long Sequence,
    string TraceId,
    string SpanId,
    string? ParentId,
    string Name,
    string Kind,
    DateTimeOffset StartUtc,
    double DurationMs,
    bool Error,
    IReadOnlyDictionary<string, string> Details);
