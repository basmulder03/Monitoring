namespace Monitoring.Agent.Features.Configuration;

/// <summary>Settings of this agent that can take the place of the central ones, but only for the settings that the central configuration marks as overridable.</summary>
public sealed class AgentConfigurationOptions
{
    /// <summary>Gets or sets how often the metrics of the applications are pulled, here and now; <see langword="null"/> for what the controller says.</summary>
    public int? PullSeconds { get; set; }

    /// <summary>Gets or sets how often the calls of the applications to their dependencies are pulled, here and now; <see langword="null"/> for what the controller says.</summary>
    public int? EdgeFlushSeconds { get; set; }
}

/// <summary>What applies to the applications of one service: how often to pull what.</summary>
/// <param name="PullSeconds">How often the metrics are pulled.</param>
/// <param name="EdgeFlushSeconds">How often the calls to dependencies are pulled.</param>
public sealed record EffectiveSettings(int PullSeconds, int EdgeFlushSeconds);
