using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Metrics;

/// <summary>Answers pulls for the application's metrics.</summary>
/// <param name="collector">Holds the latest values.</param>
internal sealed class MetricsPullSource(MetricsCollector collector) : IPullSource
{
    /// <inheritdoc />
    public PullKind Kind => PullKind.Metrics;

    /// <inheritdoc />
    public ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct) =>
        ValueTask.FromResult(new PullData(ReportPayload.Write(collector.Read()), cursor));
}
