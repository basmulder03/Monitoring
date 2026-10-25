namespace Plover.Controller.Features.Alerting;

/// <summary>
/// Tells how much counters that only grow have grown within a window. A rule that looks for a rate cannot ask the application for one, because the controller keeps
/// only the latest value of each counter, so it watches the values change instead; what it watched is lost when the controller restarts and is rebuilt within a window.
/// </summary>
/// <param name="window">How far back a change counts.</param>
/// <param name="time">Clock for the samples.</param>
internal sealed class RollingCounters(TimeSpan window, TimeProvider time)
{
    private readonly Dictionary<string, Queue<(DateTimeOffset At, double Value)>> _samples = [];
    private readonly HashSet<string> _seen = [];

    /// <summary>Records the value of a counter now.</summary>
    /// <param name="key">Which counter.</param>
    /// <param name="value">Its value.</param>
    /// <returns>How much it has grown within the window; 0 the first time it is seen, so what happened before the controller watched does not count.</returns>
    public double Observe(string key, double value)
    {
        var now = time.GetUtcNow();
        _seen.Add(key);
        if (!_samples.TryGetValue(key, out var samples))
        {
            _samples[key] = samples = new Queue<(DateTimeOffset, double)>();
        }

        // A counter that went down was reset by a restart of the application: what it grew before is no use as a starting point.
        if (samples.Count > 0 && value < samples.Last().Value)
        {
            samples.Clear();
        }

        samples.Enqueue((now, value));

        // Keep one sample from before the window as its starting point.
        while (samples.Count > 1 && now - samples.ElementAt(1).At >= window)
        {
            samples.Dequeue();
        }

        return value - samples.Peek().Value;
    }

    /// <summary>Forgets the counters that were not observed since the previous call, so the samples never outgrow what exists.</summary>
    public void ForgetUnseen()
    {
        foreach (var key in _samples.Keys.Where(key => !_seen.Contains(key)).ToList())
        {
            _samples.Remove(key);
        }

        _seen.Clear();
    }
}
