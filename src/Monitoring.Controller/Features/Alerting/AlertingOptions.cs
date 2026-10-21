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

    /// <summary>How many restarts of an instance within <see cref="RestartLoopWindow"/> raise the restart-loop alert.</summary>
    public int RestartLoopCount { get; set; } = 3;

    /// <summary>The time within which <see cref="RestartLoopCount"/> restarts count as a loop.</summary>
    public TimeSpan RestartLoopWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Silences that keep matching alerts out of the active list. The conditions are still evaluated, so an alert reappears when its silence ends.</summary>
    public List<AlertSilence> Silences { get; set; } = [];

    /// <summary>How long a vulnerable package version must be in use before its alert fires; zero fires at once.</summary>
    public TimeSpan VulnerablePackageWait { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// How long a package must stay in versions its alignment rule does not allow before the alert fires. A rollout that updates a library
    /// across services passes through such a state, so this is long by default.
    /// </summary>
    public TimeSpan PackageDriftWait { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the rules are evaluated.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(5);
}
