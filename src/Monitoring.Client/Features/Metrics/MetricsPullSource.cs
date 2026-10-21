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
    public ValueTask<string?> ReadAsync(CancellationToken ct) => ValueTask.FromResult<string?>(ReportPayload.Write(collector.Read()));
}
