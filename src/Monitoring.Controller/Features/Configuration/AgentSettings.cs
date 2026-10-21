namespace Monitoring.Controller.Features.Configuration;

/// <summary>The settings the controller hands to one agent, after every layer of the central configuration has been applied.</summary>
/// <param name="Version">Identifies this exact combination of settings, so the controller can tell whether an agent has applied it.</param>
/// <param name="PullSeconds">How often an agent pulls the metrics of its applications.</param>
/// <param name="EdgeFlushSeconds">How often an agent pulls the calls of its applications to their dependencies.</param>
public sealed record AgentSettings(string Version, int PullSeconds, int EdgeFlushSeconds);
