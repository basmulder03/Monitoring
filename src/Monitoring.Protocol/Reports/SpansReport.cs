namespace Monitoring.Protocol.Reports;

/// <summary>Recent spans of an application; the answer to a pull for <see cref="Messages.PullKind.Spans"/>.</summary>
/// <param name="Spans">The spans newer than the cursor of the pull, oldest first.</param>
public sealed record SpansReport(IReadOnlyList<SpanReport> Spans);
