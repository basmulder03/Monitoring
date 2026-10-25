using Plover.Protocol.Health;

namespace Plover.Controller.Features.Health;

/// <summary>Health of one service within a scope.</summary>
/// <param name="ServiceName">Name of the service.</param>
/// <param name="Health">Worst health of the live instances; Unhealthy when the service has no live instance.</param>
/// <param name="LiveInstances">Number of instances that are Starting or Running.</param>
/// <param name="HealthyInstances">Number of live instances that are Healthy.</param>
public sealed record ServiceHealth(string ServiceName, HealthState Health, int LiveInstances, int HealthyInstances);
