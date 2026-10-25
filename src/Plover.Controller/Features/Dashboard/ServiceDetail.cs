using Plover.Protocol.Health;

namespace Plover.Controller.Features.Dashboard;

/// <summary>A service in a scope with its instances.</summary>
/// <param name="Name">Name of the service.</param>
/// <param name="Health">Worst health of the live instances; Unhealthy when none is live.</param>
/// <param name="LiveInstances">Number of live instances.</param>
/// <param name="HealthyInstances">Number of live instances that are Healthy.</param>
/// <param name="Instances">All instances of the service, including Gone ones, ordered by id.</param>
public sealed record ServiceDetail(
    string Name,
    HealthState Health,
    int LiveInstances,
    int HealthyInstances,
    IReadOnlyList<InstanceDetail> Instances);
