using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;

namespace Monitoring.Agent.Features.Discovery.Windows;

/// <summary>Turns the state of a Windows service into the instance the controller shows.</summary>
internal static class WindowsServiceMapper
{
    /// <summary>The metric that counts how often the service came back after not running.</summary>
    public const string RestartsMetric = "windows.service.restarts";

    /// <summary>Maps a service to an instance.</summary>
    /// <param name="service">The service as the Service Control Manager reports it.</param>
    /// <param name="restarts">How many times the agent has seen the service start again after it was not running.</param>
    /// <returns>The instance.</returns>
    /// <param name="process">What the process of the service uses; <see langword="null"/> when it does not run or cannot be read.</param>
    public static InstanceInfo Map(WindowsServiceStatus service, int restarts, ProcessSample? process = null)
    {
        var (lifecycle, health) = service.State switch
        {
            WindowsServiceState.Running => (InstanceLifecycle.Running, HealthState.Healthy),
            WindowsServiceState.Paused => (InstanceLifecycle.Running, HealthState.Degraded),
            WindowsServiceState.StartPending or WindowsServiceState.ContinuePending => (InstanceLifecycle.Starting, HealthState.Unknown),
            WindowsServiceState.StopPending or WindowsServiceState.PausePending => (InstanceLifecycle.Terminating, HealthState.Unknown),

            // A stopped service is still an instance of its service that is not serving, so it counts against the health of the service.
            _ => (InstanceLifecycle.Running, HealthState.Unhealthy),
        };

        return new InstanceInfo(
            service.Name,
            service.Name,
            lifecycle,
            health,
            new Dictionary<string, string>
            {
                ["windows.display-name"] = service.DisplayName,
                ["windows.state"] = service.State.ToString(),
                ["windows.start-type"] = service.StartType.ToString(),
            },
            Checks: service.State == WindowsServiceState.Stopped ? [new HealthCheckResult("windows service", HealthState.Unhealthy, CauseOfStop(service))] : null,
            Metrics: Metrics(restarts, process));
    }

    private static List<MetricSeries> Metrics(int restarts, ProcessSample? process)
    {
        var none = new Dictionary<string, string>();
        List<MetricSeries> metrics = [new(RestartsMetric, none, restarts)];
        if (process is not null)
        {
            metrics.Add(new MetricSeries("windows.process.memory_bytes", none, process.MemoryBytes));
            metrics.Add(new MetricSeries("windows.process.threads", none, process.Threads));
            metrics.Add(new MetricSeries("windows.process.handles", none, process.Handles));
        }

        return metrics;
    }

    private static string CauseOfStop(WindowsServiceStatus service) => service.StartType switch
    {
        WindowsServiceStartType.Automatic => "The service is stopped, although its start type is Automatic, so the Service Control Manager should have it running.",
        WindowsServiceStartType.Disabled => "The service is stopped and its start type is Disabled, so it cannot be started.",
        _ => $"The service is stopped; its start type is {service.StartType}.",
    };
}
