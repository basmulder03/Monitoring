using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;

namespace Plover.Controller.Features.Alerting;

/// <summary>Fires for a running instance whose health is Unhealthy.</summary>
/// <param name="registry">Source of the instances.</param>
/// <param name="options">Provides the wait time.</param>
internal sealed class InstanceUnhealthyRule(IScopeRegistry registry, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => "instance-unhealthy";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => options.InstanceUnhealthyWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        // Only Running: a Starting instance is expected to be unhealthy until it registers, and a
        // Terminating one is leaving on purpose.
        return registry.GetScopeNames().SelectMany(scope => registry.GetInstances(scope)
            .Where(i => i.Lifecycle == InstanceLifecycle.Running && i.Health == HealthState.Unhealthy)
            .Select(i => $"{scope}/{i.ServiceName}/{i.Id}"));
    }
}
