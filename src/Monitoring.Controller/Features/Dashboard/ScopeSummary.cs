using Monitoring.Protocol.Health;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>One tile of the overview: how a scope (a namespace or a server) is doing.</summary>
/// <param name="Name">Name of the scope.</param>
/// <param name="Health">Worst health of the scope's services.</param>
/// <param name="Services">Number of services in the scope.</param>
/// <param name="Instances">Number of live instances (Starting or Running).</param>
/// <param name="Problems">Number of live instances that are not Healthy.</param>
/// <param name="Labels">Labels of the scope, such as its environment, which the page groups and filters by.</param>
/// <param name="AgentConnected">Whether the scope's agent is alive. When not, <paramref name="Health"/> is Unknown and the counts are the last known.</param>
/// <param name="LastSeen">When the agent was last heard from; only set while it is not connected, so that it stays fixed and does not make every heartbeat a change.</param>
public sealed record ScopeSummary(
    string Name,
    HealthState Health,
    int Services,
    int Instances,
    int Problems,
    IReadOnlyDictionary<string, string> Labels,
    bool AgentConnected,
    DateTimeOffset? LastSeen);
