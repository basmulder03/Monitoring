using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Health;

/// <summary>Answers pulls for the results of the application's health checks.</summary>
/// <param name="reporter">Runs the checks.</param>
internal sealed class HealthPullSource(HealthReporter reporter) : IPullSource
{
    /// <inheritdoc />
    public PullKind Kind => PullKind.Health;

    /// <inheritdoc />
    public async ValueTask<string?> ReadAsync(CancellationToken ct) =>
        ReportPayload.Write(await reporter.ReportAsync(ct).ConfigureAwait(false));
}
