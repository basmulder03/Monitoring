using System.Globalization;

namespace Plover.Tests.Shared;

/// <summary>
/// Where the budget tests leave what they measured, for the nightly run to keep and compare (<c>scripts/perf-trend.py</c>). Does nothing unless
/// <c>PLOVER_PERF_RESULTS</c> names a file, so an ordinary test run is not changed by it. A smaller number is always better.
/// </summary>
public static class PerfResults
{
    private static readonly Lock Gate = new();

    /// <summary>Appends one measurement as a line of tab-separated name, value and unit.</summary>
    /// <param name="name">Stable name of the measurement, like <c>controller.recovery</c>; the trend is kept under it.</param>
    /// <param name="value">What was measured; smaller is better.</param>
    /// <param name="unit">Unit of the value: ms, us, MB, KB or B.</param>
    public static void Record(string name, double value, string unit)
    {
        if (Environment.GetEnvironmentVariable("PLOVER_PERF_RESULTS") is not { Length: > 0 } path)
        {
            return;
        }

        // Tests of several classes write to the same file; a lock is enough because they share one process.
        lock (Gate)
        {
            File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture, $"{name}\t{value}\t{unit}\n"));
        }
    }
}
