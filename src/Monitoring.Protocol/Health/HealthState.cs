namespace Monitoring.Protocol.Health;

/// <summary>Health of an entity. Unknown means the link to its agent is down, not that the workload is.</summary>
public enum HealthState : byte
{
    /// <summary>All checks pass.</summary>
    Healthy = 0,

    /// <summary>The state cannot be determined, for example because the scope's agent is disconnected.</summary>
    Unknown = 1,

    /// <summary>A non-critical check fails.</summary>
    Degraded = 2,

    /// <summary>A critical check fails or heartbeats are missed.</summary>
    Unhealthy = 3,
}
