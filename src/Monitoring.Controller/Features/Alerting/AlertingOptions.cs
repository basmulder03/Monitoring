namespace Monitoring.Controller.Features.Alerting;

/// <summary>Settings of the alerting slice.</summary>
public sealed class AlertingOptions
{
    /// <summary>
    /// How long after startup no rule is evaluated, so the snapshots of the agents can arrive first.
    /// About twice the snapshot interval.
    /// </summary>
    public TimeSpan StartupGrace { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long an instance must stay Unhealthy before the alert fires.</summary>
    public TimeSpan InstanceUnhealthyWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a service must stay without a serving instance before the alert fires.</summary>
    public TimeSpan ServiceDownWait { get; set; } = TimeSpan.Zero;

    /// <summary>How long the agent of a scope must stay gone before the alert fires.</summary>
    public TimeSpan ScopeUnknownWait { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the rules are evaluated.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(5);
}
