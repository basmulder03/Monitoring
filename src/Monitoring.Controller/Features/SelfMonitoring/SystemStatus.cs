namespace Monitoring.Controller.Features.SelfMonitoring;

/// <summary>The state of this controller replica.</summary>
/// <param name="Replica">The host name of the replica.</param>
/// <param name="UptimeSeconds">How long the replica has been running.</param>
/// <param name="Scopes">How many scopes have reported.</param>
/// <param name="AgentsConnected">How many of those scopes have an agent that is alive.</param>
/// <param name="EventClients">How many pages have the live stream open.</param>
/// <param name="AlertEvaluationMilliseconds">How long the latest run of the alert rules took; <see langword="null"/> before the first.</param>
/// <param name="MemoryBytes">The memory in use by the process.</param>
/// <param name="Healthy">Whether every scope has a live agent.</param>
public sealed record SystemStatus(
    string Replica,
    long UptimeSeconds,
    int Scopes,
    int AgentsConnected,
    int EventClients,
    double? AlertEvaluationMilliseconds,
    long MemoryBytes,
    bool Healthy);
