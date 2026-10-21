using Monitoring.Protocol.Health;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>One tile of the overview: how a scope (a namespace or a server) is doing.</summary>
/// <param name="Name">Name of the scope.</param>
/// <param name="Health">Worst health of the scope's services.</param>
/// <param name="Services">Number of services in the scope.</param>
/// <param name="Instances">Number of live instances (Starting or Running).</param>
/// <param name="Problems">Number of live instances that are not Healthy.</param>
public sealed record ScopeSummary(string Name, HealthState Health, int Services, int Instances, int Problems);
