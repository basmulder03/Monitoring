using Monitoring.Protocol.Health;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>A scope with its services and their instances.</summary>
/// <param name="Name">Name of the scope.</param>
/// <param name="Health">Worst health of the scope's services.</param>
/// <param name="Services">The services, most severe first, then by name.</param>
/// <param name="Labels">Labels of the scope.</param>
/// <param name="AgentConnected">Whether the scope's agent is alive. When not, what is shown is the last known state.</param>
/// <param name="LastSeen">When the agent was last heard from; only set while it is not connected.</param>
public sealed record ScopeDetail(
    string Name,
    HealthState Health,
    IReadOnlyList<ServiceDetail> Services,
    IReadOnlyDictionary<string, string> Labels,
    bool AgentConnected,
    DateTimeOffset? LastSeen);
