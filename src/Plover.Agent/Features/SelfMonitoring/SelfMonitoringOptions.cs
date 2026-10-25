namespace Plover.Agent.Features.SelfMonitoring;

/// <summary>Settings of the self-monitoring slice.</summary>
public sealed class SelfMonitoringOptions
{
    /// <summary>Gets or sets how often the agent reports about itself.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the id of the pod the agent runs in, which is how its report finds the instance of that pod.</summary>
    public string PodName { get; set; } = Environment.MachineName;
}
