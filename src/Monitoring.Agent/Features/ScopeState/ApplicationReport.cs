using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>What an application says about itself through its monitoring client and sidecar.</summary>
/// <param name="ServiceName">Name of the service the application is part of.</param>
/// <param name="Version">Version of the application; <see langword="null"/> when it does not say.</param>
/// <param name="Health">The worst result of the application's health checks.</param>
/// <param name="Checks">The result of each health check.</param>
/// <param name="Metrics">The latest value of each metric; <see langword="null"/> until the application has been asked.</param>
/// <param name="Edges">The calls to dependencies, counted since the application started; <see langword="null"/> until it has been asked.</param>
public sealed record ApplicationReport(string ServiceName, string? Version, HealthState Health, IReadOnlyList<HealthCheckResult> Checks, IReadOnlyList<MetricSeries>? Metrics = null, IReadOnlyList<EdgeReport>? Edges = null);
