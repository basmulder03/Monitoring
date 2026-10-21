using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Health;
using HealthCheckResult = Monitoring.Protocol.Health.HealthCheckResult;
using HealthReport = Monitoring.Protocol.Reports.HealthReport;

namespace Monitoring.Client.Features.Health;

/// <summary>Runs the application's health checks and turns the outcome into the report the monitoring system understands.</summary>
/// <param name="service">Runs the checks the application registered.</param>
/// <param name="registrations">Provides the tags of each check, to tell critical from non-critical ones.</param>
/// <param name="options">Provides how long the checks may take.</param>
internal sealed class HealthReporter(
    HealthCheckService service,
    IOptions<HealthCheckServiceOptions> registrations,
    MonitoringOptions options)
{
    private const int MaxDescriptionLength = 500;

    /// <summary>Runs every health check.</summary>
    /// <param name="ct">Cancels the checks.</param>
    /// <returns>The result of each check and the worst of them; Healthy for an application without checks.</returns>
    public async Task<HealthReport> ReportAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.HealthTimeout);

        Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report;
        try
        {
            report = await service.CheckHealthAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Checks that do not finish in time count as a failure, rather than hanging the reply.
            var timedOut = new HealthCheckResult("health checks", HealthState.Unhealthy, $"did not finish within {options.HealthTimeout.TotalSeconds:0.#} seconds");
            return new HealthReport(HealthState.Unhealthy, [timedOut]);
        }

        var nonCritical = registrations.Value.Registrations
            .Where(r => r.Tags.Contains(MonitoringTags.NonCritical))
            .Select(r => r.Name)
            .ToHashSet(StringComparer.Ordinal);

        var results = report.Entries
            .Select(entry => new HealthCheckResult(
                entry.Key,
                Map(entry.Value.Status, nonCritical.Contains(entry.Key)),
                Describe(entry.Value)))
            .OrderBy(result => result.Name, StringComparer.Ordinal)
            .ToList();

        return new HealthReport(results.Select(r => r.Health).Worst(), results);
    }

    private static HealthState Map(HealthStatus status, bool nonCritical) => status switch
    {
        HealthStatus.Healthy => HealthState.Healthy,
        HealthStatus.Degraded => HealthState.Degraded,
        _ => nonCritical ? HealthState.Degraded : HealthState.Unhealthy,
    };

    private static string? Describe(HealthReportEntry entry)
    {
        var text = entry.Description ?? entry.Exception?.Message;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength] + "...";
    }
}
