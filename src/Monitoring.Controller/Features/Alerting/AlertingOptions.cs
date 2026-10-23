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

    /// <summary>A certificate with fewer days left than this raises the warning <c>certificate-expiring</c>.</summary>
    public int CertificateWarningDays { get; set; } = 14;

    /// <summary>A certificate with fewer days left than this, or already expired, raises the critical alert <c>certificate-expired-soon</c> instead.</summary>
    public int CertificateCriticalDays { get; set; } = 3;

    /// <summary>How long a queue with waiting messages must stay without a consumer before the alert <c>queue-without-consumers</c> fires; consumers come and go during a rollout.</summary>
    public TimeSpan QueueWithoutConsumersWait { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a database connection pool must stay without a free connection before the alert <c>connection-pool-full</c> fires.</summary>
    public TimeSpan ConnectionPoolFullWait { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>A replica that has not acknowledged this many changes of a scope that an agent has sent raises the alert <c>replica-behind</c>.</summary>
    public int ReplicaLagThreshold { get; set; } = 25;

    /// <summary>How long a replica must stay behind before its alert fires.</summary>
    public TimeSpan ReplicaBehindWait { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a replica that an agent is not connected to must stay so before the alert <c>replica-unreachable</c> fires.</summary>
    public TimeSpan ReplicaUnreachableWait { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The window in which the calls to a dependency are looked at for failures.</summary>
    public TimeSpan EdgeErrorWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The share of calls to a dependency that must fail within the window to raise <c>edge-error-rate</c>, from 0 to 1.</summary>
    public double EdgeErrorShare { get; set; } = 0.5;

    /// <summary>The fewest calls within the window that can raise <c>edge-error-rate</c>.</summary>
    public int EdgeErrorMinCalls { get; set; } = 20;

    /// <summary>How long an agent may use another configuration than expected before <c>configuration-drift</c> fires.</summary>
    public TimeSpan ConfigurationDriftWait { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The window in which spans thrown away by a client are counted.</summary>
    public TimeSpan DroppedDataWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How many spans a client may throw away within the window before <c>dropped-data</c> fires.</summary>
    public int DroppedDataSpans { get; set; } = 100;

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
