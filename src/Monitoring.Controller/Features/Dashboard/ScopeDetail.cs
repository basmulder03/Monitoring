using Monitoring.Protocol.Health;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>A scope with its services and their instances.</summary>
/// <param name="Name">Name of the scope.</param>
/// <param name="Health">Worst health of the scope's services.</param>
/// <param name="Services">The services, most severe first, then by name.</param>
/// <param name="Labels">Labels of the scope.</param>
public sealed record ScopeDetail(
    string Name,
    HealthState Health,
    IReadOnlyList<ServiceDetail> Services,
    IReadOnlyDictionary<string, string> Labels);
