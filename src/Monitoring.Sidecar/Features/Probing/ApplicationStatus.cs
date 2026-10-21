using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.Probing;

/// <summary>What the sidecar currently knows about the application next to it.</summary>
/// <param name="Description">Who the application says it is; <see langword="null"/> until it has answered once.</param>
/// <param name="Health">
/// The application's own health report, or Unhealthy with the reason when it could not be reached,
/// because an application that cannot answer is not healthy.
/// </param>
/// <param name="LastReached">When the application last answered; <see langword="null"/> if it never did.</param>
public sealed record ApplicationStatus(InstanceDescription? Description, HealthReport Health, DateTimeOffset? LastReached);
