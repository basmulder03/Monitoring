using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;

namespace Monitoring.Protocol.Instances;

/// <summary>An instance as the agent sees it; the agent is the source of truth for existence.</summary>
/// <param name="Id">Stable instance id.</param>
/// <param name="ServiceName">Service the instance belongs to.</param>
/// <param name="Lifecycle">Position in the instance's lifetime.</param>
/// <param name="Health">Current health roll-up of the instance.</param>
/// <param name="Labels">Free-form labels of the instance.</param>
/// <param name="Version">Version of the application, as it reports itself; <see langword="null"/> while unknown.</param>
/// <param name="Checks">Results of the application's own health checks; <see langword="null"/> when it reports none.</param>
/// <param name="Metrics">The latest value of each metric of the application; <see langword="null"/> when it reports none.</param>
/// <param name="Edges">The calls the application made to its dependencies, counted since it started; <see langword="null"/> when it reports none.</param>
/// <param name="Packages">The packages the application is built from; <see langword="null"/> until it has said.</param>
/// <param name="Containers">The containers of a pod; <see langword="null"/> for instances that are not pods.</param>
public sealed record InstanceInfo(
    string Id,
    string ServiceName,
    InstanceLifecycle Lifecycle,
    HealthState Health,
    IReadOnlyDictionary<string, string> Labels,
    string? Version = null,
    IReadOnlyList<HealthCheckResult>? Checks = null,
    IReadOnlyList<MetricSeries>? Metrics = null,
    IReadOnlyList<ContainerInfo>? Containers = null,
    IReadOnlyList<EdgeReport>? Edges = null,
    IReadOnlyList<PackageReport>? Packages = null);
