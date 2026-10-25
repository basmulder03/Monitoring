namespace Plover.Common;

/// <summary>Exponential retry delays with jitter, so restarted agents do not all return at the same moment.</summary>
public static class Backoff
{
    /// <summary>Computes how long to wait before the next connection attempt.</summary>
    /// <param name="attempt">Number of consecutive failures so far; 0 for the first retry.</param>
    /// <param name="min">Shortest wait.</param>
    /// <param name="max">Longest wait.</param>
    /// <param name="random">A random number from 0 up to 1 that picks a point between the minimum and the current ceiling.</param>
    /// <returns>A wait between <paramref name="min"/> and the ceiling, which doubles per failure up to <paramref name="max"/>.</returns>
    public static TimeSpan Delay(int attempt, TimeSpan min, TimeSpan max, double random)
    {
        // Doubling more than 30 times can only exceed any sensible maximum, and would overflow.
        var doublings = Math.Clamp(attempt, 0, 30);
        var ceiling = TimeSpan.FromTicks(Math.Min(max.Ticks, min.Ticks * (1L << doublings)));
        if (ceiling < min)
        {
            ceiling = min;
        }

        return min + (ceiling - min) * Math.Clamp(random, 0, 1);
    }
}
