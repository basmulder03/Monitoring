using Monitoring.Protocol.Health;

namespace Monitoring.Protocol.Instances;

/// <summary>An instance as the agent sees it; the agent is the source of truth for existence.</summary>
/// <param name="Id">Stable instance id.</param>
/// <param name="ServiceName">Service the instance belongs to.</param>
/// <param name="Lifecycle">Position in the instance's lifetime.</param>
/// <param name="Health">Current health roll-up of the instance.</param>
/// <param name="Labels">Free-form labels of the instance.</param>
public sealed record InstanceInfo(
    string Id,
    string ServiceName,
    InstanceLifecycle Lifecycle,
    HealthState Health,
    IReadOnlyDictionary<string, string> Labels);
