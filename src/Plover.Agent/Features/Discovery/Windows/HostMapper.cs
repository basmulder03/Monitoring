using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;

namespace Plover.Agent.Features.Discovery.Windows;

/// <summary>Turns what the machine says about itself into the instance the controller shows for the host.</summary>
internal static class HostMapper
{
    /// <summary>The name of the service every host belongs to.</summary>
    public const string ServiceName = "windows-host";

    /// <summary>A disk with less than this share free is Degraded.</summary>
    public const double DiskLowShare = 0.10;

    /// <summary>A disk with less than this share free is Unhealthy.</summary>
    public const double DiskCriticalShare = 0.03;

    /// <summary>Maps a sample of the machine to an instance.</summary>
    /// <param name="host">The sample.</param>
    /// <returns>The instance, whose id is the name of the machine.</returns>
    public static InstanceInfo Map(HostSample host)
    {
        var none = new Dictionary<string, string>();
        List<MetricSeries> metrics =
        [
            new("host.uptime_seconds", none, host.UptimeSeconds),
            new("host.memory_total_bytes", none, host.MemoryTotalBytes),
            new("host.memory_available_bytes", none, host.MemoryAvailableBytes),
            new("host.pending_reboot", none, host.PendingReboot ? 1 : 0),
        ];
        if (host.CpuPercent is { } cpu)
        {
            metrics.Add(new MetricSeries("host.cpu_percent", none, cpu));
        }

        List<HealthCheckResult> checks = [];
        foreach (var disk in host.Disks)
        {
            var drive = new Dictionary<string, string> { ["drive"] = disk.Name };
            metrics.Add(new MetricSeries("host.disk_total_bytes", drive, disk.TotalBytes));
            metrics.Add(new MetricSeries("host.disk_free_bytes", drive, disk.FreeBytes));

            var free = disk.TotalBytes == 0 ? 1.0 : (double)disk.FreeBytes / disk.TotalBytes;
            if (free < DiskLowShare)
            {
                checks.Add(new HealthCheckResult(
                    $"disk {disk.Name}",
                    free < DiskCriticalShare ? HealthState.Unhealthy : HealthState.Degraded,
                    $"{free:P0} of the disk is free ({disk.FreeBytes / 1_048_576} MB of {disk.TotalBytes / 1_048_576} MB)."));
            }
        }

        return new InstanceInfo(
            host.MachineName,
            ServiceName,
            InstanceLifecycle.Running,
            checks.Select(check => check.Health).Worst(),
            new Dictionary<string, string> { ["windows.os"] = host.OperatingSystem },
            Checks: checks.Count > 0 ? checks : null,
            Metrics: metrics);
    }
}
