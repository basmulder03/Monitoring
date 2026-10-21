namespace Monitoring.Protocol.Health;

/// <summary>The outcome of one health check of an application.</summary>
/// <param name="Name">Name of the check, such as <c>database</c>.</param>
/// <param name="Health">Healthy when it passes, Degraded when a non-critical check fails, Unhealthy when a critical one does.</param>
/// <param name="Description">Why the check failed or what it found; <see langword="null"/> when there is nothing to add.</param>
public sealed record HealthCheckResult(string Name, HealthState Health, string? Description = null);
