using Monitoring.Protocol.Health;

namespace Monitoring.Protocol.Reports;

/// <summary>The health of an application; the answer to a pull for <see cref="Messages.PullKind.Health"/>.</summary>
/// <param name="Health">The worst result of all checks, Healthy when there are none.</param>
/// <param name="Checks">The result of each check.</param>
public sealed record HealthReport(HealthState Health, IReadOnlyList<HealthCheckResult> Checks);
