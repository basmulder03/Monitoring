using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for a service that has no instance able to serve.</summary>
/// <remarks>
/// An instance can serve when it is live (Starting or Running) and Healthy or Degraded: a failing non-critical
/// check lowers health but the service still works.
/// </remarks>
/// <param name="registry">Source of the instances.</param>
/// <param name="options">Provides the wait time.</param>
internal sealed class ServiceDownRule(IScopeRegistry registry, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => "service-down";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Critical;

    /// <inheritdoc />
    public TimeSpan Wait => options.ServiceDownWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        // Group every instance, including Gone ones, so a service whose instances all disappeared is reported down.
        return registry.GetScopeNames().SelectMany(scope => registry.GetInstances(scope)
            .GroupBy(i => i.ServiceName)
            .Where(group => !group.Any(CanServe))
            .Select(group => $"{scope}/{group.Key}"));
    }

    private static bool CanServe(InstanceInfo instance) =>
        instance.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running
        && instance.Health is HealthState.Healthy or HealthState.Degraded;
}
