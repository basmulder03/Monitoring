namespace Plover.Controller.Features.Configuration;

/// <summary>The settings the controller hands to one agent, after every layer of the central configuration has been applied.</summary>
/// <param name="Version">Identifies this exact combination of settings, so the controller can tell whether an agent has applied it.</param>
/// <param name="PullSeconds">How often an agent pulls the metrics of its applications.</param>
/// <param name="EdgeFlushSeconds">How often an agent pulls the calls of its applications to their dependencies.</param>
/// <param name="Services">The settings of the services in the scope that differ from the ones above, by service name; none when no service is set apart.</param>
/// <param name="Overridable">The settings the agent may be given another value for locally.</param>
public sealed record AgentSettings(
    string Version,
    int PullSeconds,
    int EdgeFlushSeconds,
    IReadOnlyDictionary<string, ServiceSettings>? Services = null,
    IReadOnlyList<string>? Overridable = null);

/// <summary>The settings of one service, which replace the ones of the agent for the applications of that service.</summary>
/// <param name="PullSeconds">How often an agent pulls the metrics of the applications of the service.</param>
/// <param name="EdgeFlushSeconds">How often an agent pulls their calls to their dependencies.</param>
public sealed record ServiceSettings(int PullSeconds, int EdgeFlushSeconds);
