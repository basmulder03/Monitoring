using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;

namespace Monitoring.Agent.Features.SelfMonitoring;

/// <summary>
/// Reports the agent as the application of its own pod at a fixed interval, so its values travel to the controllers in the same
/// snapshots and deltas as everything else. Like any report it adds to a pod the platform knows about; it does not make one exist.
/// </summary>
/// <param name="telemetry">What the other slices counted.</param>
/// <param name="store">Receives the report and tells how many instances are watched.</param>
/// <param name="options">Provides the interval and the pod.</param>
/// <param name="time">Clock for the interval and the uptime.</param>
/// <param name="logger">Receives failures.</param>
internal sealed class SelfReportService(
    AgentTelemetry telemetry, IScopeStateStore store, SelfMonitoringOptions options, TimeProvider time, ILogger<SelfReportService> logger) : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();
    private static readonly string Version = typeof(SelfReportService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private readonly long _started = time.GetTimestamp();
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here, so the interval counts from the start of the service.
        _timer = new PeriodicTimer(options.Interval, time);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <summary>Builds the values the agent reports about itself now.</summary>
    /// <returns>One series per value.</returns>
    public IReadOnlyList<MetricSeries> Read()
    {
        using var process = Process.GetCurrentProcess();
        var own = options.PodName;
        List<MetricSeries> series =
        [
            new("monitoring.agent.memory_bytes", NoLabels, Environment.WorkingSet),
            new("monitoring.agent.cpu_seconds", NoLabels, process.TotalProcessorTime.TotalSeconds),
            new("monitoring.agent.uptime_seconds", NoLabels, time.GetElapsedTime(_started).TotalSeconds),
            new("monitoring.agent.instances_watched", NoLabels, store.CreateSnapshot().Instances.Count(instance => instance.Id != own)),
        ];
        var values = telemetry.Read();
        series.AddRange(values.Where(pair => pair.Key.Replica is null).OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .Select(pair => new MetricSeries(pair.Key.Name, NoLabels, pair.Value)));

        // How far each replica is behind: the sequence of the scope now, less the last one the replica acknowledged. A replica that is not connected has no lag to speak of.
        var sequence = store.CreateSnapshot().Sequence;
        foreach (var replica in values.Keys.Where(key => key.Replica is not null).Select(key => key.Replica!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var labels = new Dictionary<string, string> { ["replica"] = replica };
            var connected = values.GetValueOrDefault((ReplicaMetrics.Connected, replica));
            series.Add(new MetricSeries(ReplicaMetrics.Connected, labels, connected));
            if (connected > 0)
            {
                series.Add(new MetricSeries(ReplicaMetrics.Lag, labels, Math.Max(0, sequence - values.GetValueOrDefault((ReplicaMetrics.AckedSequence, replica)))));
            }
        }

        return series;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                try
                {
                    store.Report(options.PodName, new ApplicationReport("monitoring-agent", Version, HealthState.Healthy, [], Read()));
                    store.Publish();
                }
                catch (Exception ex)
                {
                    // Reporting about itself must never take the agent down.
                    logger.LogError(ex, "Reporting the state of the agent failed");
                }
            }
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }
}
