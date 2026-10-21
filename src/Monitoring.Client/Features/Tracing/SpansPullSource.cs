using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Answers pulls for the application's recent spans.</summary>
/// <param name="spans">Holds the spans.</param>
internal sealed class SpansPullSource(SpanBuffer spans) : IPullSource
{
    /// <inheritdoc />
    public PullKind Kind => PullKind.Spans;

    /// <inheritdoc />
    public ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct)
    {
        var (found, next) = spans.ReadAfter(cursor);
        return ValueTask.FromResult(new PullData(ReportPayload.Write(new SpansReport(found)), next));
    }
}
