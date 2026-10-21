using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>Compares instances by what they say, which record equality cannot do because it compares dictionaries and lists by reference.</summary>
internal static class InstanceEquality
{
    /// <summary>Tells whether two instances say the same.</summary>
    /// <param name="a">One instance.</param>
    /// <param name="b">The other instance.</param>
    /// <returns><see langword="true"/> when every field, including labels and checks, is the same.</returns>
    public static bool AreEqual(InstanceInfo a, InstanceInfo b) =>
        a.Id == b.Id
        && a.ServiceName == b.ServiceName
        && a.Lifecycle == b.Lifecycle
        && a.Health == b.Health
        && a.Version == b.Version
        && a.Labels.Count == b.Labels.Count
        && a.Labels.All(label => b.Labels.TryGetValue(label.Key, out var value) && value == label.Value)
        && ChecksAreEqual(a.Checks, b.Checks)
        && MetricsAreEqual(a.Metrics, b.Metrics);

    private static bool ChecksAreEqual(IReadOnlyList<Monitoring.Protocol.Health.HealthCheckResult>? a, IReadOnlyList<Monitoring.Protocol.Health.HealthCheckResult>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Count == b.Count && a.Zip(b).All(pair => pair.First == pair.Second);
    }

    private static bool MetricsAreEqual(IReadOnlyList<MetricSeries>? a, IReadOnlyList<MetricSeries>? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return a.Count == b.Count && a.Zip(b).All(pair =>
            pair.First.Name == pair.Second.Name
            && pair.First.Value.Equals(pair.Second.Value)
            && pair.First.Labels.Count == pair.Second.Labels.Count
            && pair.First.Labels.All(label => pair.Second.Labels.TryGetValue(label.Key, out var value) && value == label.Value));
    }
}
