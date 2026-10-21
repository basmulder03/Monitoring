using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Health;

/// <summary>Default <see cref="IHealthRollup"/>, computed on demand from the registry so it can never drift from it.</summary>
/// <param name="registry">Source of the instances per scope.</param>
internal sealed class HealthRollup(IScopeRegistry registry) : IHealthRollup
{
    /// <inheritdoc />
    public IReadOnlyList<ServiceHealth> GetServiceHealth(string scopeName)
    {
        // Group all instances, not only live ones, so a service whose instances are all gone still shows up as down.
        return
        [
            .. registry.GetInstances(scopeName)
                .GroupBy(instance => instance.ServiceName)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => Summarize(group.Key, group.Where(IsLive).ToList())),
        ];
    }

    /// <inheritdoc />
    public HealthState GetScopeHealth(string scopeName)
    {
        var services = GetServiceHealth(scopeName);
        return services.Count == 0 ? HealthState.Unknown : services.Select(s => s.Health).Worst();
    }

    private static bool IsLive(InstanceInfo instance) =>
        instance.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running;

    private static ServiceHealth Summarize(string serviceName, IReadOnlyList<InstanceInfo> live)
    {
        // A service with no live instance is down, which is what the "service down" alert rule keys on.
        var health = live.Count == 0 ? HealthState.Unhealthy : live.Select(i => i.Health).Worst();
        return new ServiceHealth(serviceName, health, live.Count, live.Count(i => i.Health == HealthState.Healthy));
    }
}
