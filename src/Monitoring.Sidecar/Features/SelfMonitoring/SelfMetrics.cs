using System.Diagnostics;
using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.SelfMonitoring;

/// <summary>Default <see cref="ISelfMetrics"/>: the process of the sidecar and its last question to the application.</summary>
/// <param name="time">Clock for the uptime.</param>
internal sealed class SelfMetrics(TimeProvider time) : ISelfMetrics
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private readonly long _started = time.GetTimestamp();
    private long _pullTicks;
    private int _answered;

    /// <inheritdoc />
    public void LibraryPulled(TimeSpan took, bool answered)
    {
        Interlocked.Exchange(ref _pullTicks, took.Ticks);
        Volatile.Write(ref _answered, answered ? 1 : 0);
    }

    /// <inheritdoc />
    public IReadOnlyList<MetricSeries> Read()
    {
        using var process = Process.GetCurrentProcess();
        return
        [
            new("monitoring.sidecar.memory_bytes", NoLabels, Environment.WorkingSet),
            new("monitoring.sidecar.cpu_seconds", NoLabels, process.TotalProcessorTime.TotalSeconds),
            new("monitoring.sidecar.uptime_seconds", NoLabels, time.GetElapsedTime(_started).TotalSeconds),
            new("monitoring.sidecar.library_pull_ms", NoLabels, TimeSpan.FromTicks(Interlocked.Read(ref _pullTicks)).TotalMilliseconds),
            new("monitoring.sidecar.library_connected", NoLabels, Volatile.Read(ref _answered)),
        ];
    }
}
