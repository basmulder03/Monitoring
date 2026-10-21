using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Answers pulls for the application's dependency edges.</summary>
/// <param name="edges">Holds the counters.</param>
internal sealed class EdgesPullSource(EdgeCounters edges) : IPullSource
{
    /// <inheritdoc />
    public PullKind Kind => PullKind.Edges;

    /// <inheritdoc />
    public ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct) =>
        ValueTask.FromResult(new PullData(ReportPayload.Write(edges.Read()), cursor));
}
