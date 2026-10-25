using System.Diagnostics;
using Plover.Protocol.Reports;

namespace Plover.Sidecar.Features.SelfMonitoring;

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
            new("plover.sidecar.memory_bytes", NoLabels, Environment.WorkingSet),
            new("plover.sidecar.cpu_seconds", NoLabels, process.TotalProcessorTime.TotalSeconds),
            new("plover.sidecar.uptime_seconds", NoLabels, time.GetElapsedTime(_started).TotalSeconds),
            new("plover.sidecar.library_pull_ms", NoLabels, TimeSpan.FromTicks(Interlocked.Read(ref _pullTicks)).TotalMilliseconds),
            new("plover.sidecar.library_connected", NoLabels, Volatile.Read(ref _answered)),
        ];
    }
}
