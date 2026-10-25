using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Configuration;
using Plover.Controller.Features.Health;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;

namespace Plover.Controller.Features.Dashboard;

/// <summary>Reads the controller's state through the public contracts of the other slices and shapes it for the pages.</summary>
/// <param name="registry">The instances per scope.</param>
/// <param name="health">The health roll-up per service and scope.</param>
/// <param name="alerts">The alerts that are firing.</param>
/// <param name="liveness">Tells whether each scope's agent is alive.</param>
/// <param name="configurations">Tells whether each scope's agent uses the configuration it should.</param>
/// <param name="options">Provides the label names to group by.</param>
internal sealed class DashboardModel(
    IScopeRegistry registry,
    IHealthRollup health,
    IAlertState alerts,
    IAgentLiveness liveness,
    IAppliedConfigurations configurations,
    DashboardOptions options)
{
    /// <summary>Builds the overview: a summary per scope and the alerts that are firing.</summary>
    /// <returns>The overview.</returns>
    public Overview BuildOverview()
    {
        var scopes = registry.GetScopeNames().Select(Summarize)
            .OrderByDescending(scope => scope.Health)
            .ThenBy(scope => scope.Name, StringComparer.Ordinal)
            .ToList();

        // GetActiveAlerts already returns the most severe first.
        var firing = alerts.GetActiveAlerts()
            .Select(a => new AlertSummary(a.Key, a.RuleName, a.Subject, a.Severity, a.Since, a.Acknowledged))
            .ToList();

        return new Overview(scopes, firing, [.. options.GroupBy]);
    }

    /// <summary>Builds the detail of one scope.</summary>
    /// <param name="scopeName">Name of the scope.</param>
    /// <returns>The scope with its services and instances, or <see langword="null"/> when no such scope is known.</returns>
    public ScopeDetail? BuildScope(string scopeName)
    {
        if (!registry.GetScopeNames().Contains(scopeName))
        {
            return null;
        }

        var instancesByService = registry.GetInstances(scopeName)
            .GroupBy(i => i.ServiceName)
            .ToDictionary(group => group.Key, group => group.ToList());

        var services = health.GetServiceHealth(scopeName)
            .Select(service => new ServiceDetail(
                service.ServiceName,
                service.Health,
                service.LiveInstances,
                service.HealthyInstances,
                [.. instancesByService[service.ServiceName].OrderBy(i => i.Id, StringComparer.Ordinal).Select(ToDetail)]))
            .OrderByDescending(service => service.Health)
            .ThenBy(service => service.Name, StringComparer.Ordinal)
            .ToList();

        var agent = liveness.GetScopeLiveness(scopeName);
        return new ScopeDetail(
            scopeName,
            health.GetScopeHealth(scopeName),
            services,
            new Dictionary<string, string>(registry.GetScopeLabels(scopeName)),
            agent.IsLive,
            agent.IsLive ? null : agent.LastSeen,
            ConfigurationOf(scopeName));
    }

    private ScopeConfiguration ConfigurationOf(string scopeName)
    {
        var status = configurations.StatusOf(scopeName, registry.GetScopeLabels(scopeName));
        return new ScopeConfiguration(status.Applied, status.Expected, status.InSync);
    }

    private ScopeSummary Summarize(string scopeName)
    {
        var live = registry.GetInstances(scopeName).Where(IsLive).ToList();
        var agent = liveness.GetScopeLiveness(scopeName);
        return new ScopeSummary(
            scopeName,
            health.GetScopeHealth(scopeName),
            health.GetServiceHealth(scopeName).Count,
            live.Count,
            live.Count(instance => instance.Health != HealthState.Healthy),
            new Dictionary<string, string>(registry.GetScopeLabels(scopeName)),
            agent.IsLive,
            agent.IsLive ? null : agent.LastSeen,
            ConfigurationOf(scopeName).InSync);
    }

    private static bool IsLive(InstanceInfo instance) =>
        instance.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running;

    private static InstanceDetail ToDetail(InstanceInfo instance) =>
        new(
            instance.Id,
            instance.Lifecycle,
            instance.Health,
            instance.Labels,
            instance.Version,
            instance.Checks ?? [],
            instance.Containers ?? [],
            instance.Metrics ?? []);
}
