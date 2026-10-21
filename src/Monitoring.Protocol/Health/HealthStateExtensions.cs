namespace Monitoring.Protocol.Health;

/// <summary>Roll-up helpers for <see cref="HealthState"/>.</summary>
public static class HealthStateExtensions
{
    /// <summary>Worst-of roll-up: Unhealthy &gt; Degraded &gt; Unknown &gt; Healthy.</summary>
    /// <param name="a">First state.</param>
    /// <param name="b">Second state.</param>
    /// <returns>The more severe of the two states.</returns>
    public static HealthState Worst(this HealthState a, HealthState b) => (HealthState)Math.Max((byte)a, (byte)b);

    /// <param name="states">States to combine.</param>
    /// <returns>The most severe state, or <see cref="HealthState.Healthy"/> when the sequence is empty.</returns>
    public static HealthState Worst(this IEnumerable<HealthState> states)
    {
        var result = HealthState.Healthy;
        foreach (var state in states)
        {
            result = result.Worst(state);
        }
        return result;
    }
}
