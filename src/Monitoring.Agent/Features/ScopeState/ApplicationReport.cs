using Monitoring.Protocol.Health;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>What an application says about itself through its monitoring client and sidecar.</summary>
/// <param name="ServiceName">Name of the service the application is part of.</param>
/// <param name="Version">Version of the application; <see langword="null"/> when it does not say.</param>
/// <param name="Health">The worst result of the application's health checks.</param>
/// <param name="Checks">The result of each health check.</param>
public sealed record ApplicationReport(string ServiceName, string? Version, HealthState Health, IReadOnlyList<HealthCheckResult> Checks);
