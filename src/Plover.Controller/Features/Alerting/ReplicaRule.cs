using Plover.Controller.Features.Registry;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;

namespace Plover.Controller.Features.Alerting;

/// <summary>
/// Fires for a controller replica that the agents report trouble with: one that is behind in what it has acknowledged, or one that agents cannot reach.
/// Every replica sees this from every agent, so any of them can raise it, and one that is itself unreachable is still found out by the others.
/// </summary>
/// <param name="name">The name of the rule.</param>
/// <param name="registry">Source of the instances, among them the agents.</param>
/// <param name="wait">How long the trouble must last before the alert fires.</param>
/// <param name="failing">Tells whether what an agent says about a replica is trouble.</param>
internal sealed class ReplicaRule(string name, IScopeRegistry registry, TimeSpan wait, Func<ReplicaReport, bool> failing) : IAlertRule
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => wait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects() =>
        registry.GetScopeNames()
            .SelectMany(scope => registry.GetInstances(scope))
            .Where(instance => instance.Lifecycle != InstanceLifecycle.Gone && instance.ServiceName == ReplicaMetrics.AgentService)
            .SelectMany(instance => ReplicaMetrics.From(instance.Metrics))
            .Where(failing)
            .Select(report => report.Replica)
            .Distinct(StringComparer.Ordinal);
}
