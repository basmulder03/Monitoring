using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;

namespace Plover.Controller.Features.Dashboard;

/// <summary>One instance (a pod or a process) of a service.</summary>
/// <param name="Id">Id of the instance.</param>
/// <param name="Lifecycle">Where the instance is in its lifetime; Gone instances are kept briefly so the page can show what disappeared.</param>
/// <param name="Health">Health of the instance.</param>
/// <param name="Labels">Labels of the instance, such as its node.</param>
/// <param name="Version">Version the application reports; <see langword="null"/> while unknown.</param>
/// <param name="Checks">The results of the application's own health checks; empty when it reports none.</param>
/// <param name="Containers">The containers of a pod; empty for instances that are not pods.</param>
/// <param name="Metrics">The latest value of each metric of the application; empty when it reports none.</param>
public sealed record InstanceDetail(
    string Id,
    InstanceLifecycle Lifecycle,
    HealthState Health,
    IReadOnlyDictionary<string, string> Labels,
    string? Version,
    IReadOnlyList<HealthCheckResult> Checks,
    IReadOnlyList<ContainerInfo> Containers,
    IReadOnlyList<MetricSeries> Metrics);
