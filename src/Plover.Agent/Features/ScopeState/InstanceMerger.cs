using Plover.Protocol.Health;
using Plover.Protocol.Instances;

namespace Plover.Agent.Features.ScopeState;

/// <summary>
/// Combines what the platform says about an instance (for a pod: that it exists, its state and labels) with what the
/// application says about itself. The platform decides whether the instance exists; the application adds detail.
/// </summary>
internal static class InstanceMerger
{
    /// <summary>Merges a platform view with the application's report.</summary>
    /// <param name="platform">The instance as the platform sees it.</param>
    /// <param name="application">What the application reported, or <see langword="null"/> when it never did.</param>
    /// <param name="sidecarLost">Whether the application's sidecar was connected once but is not any more.</param>
    /// <returns>The instance to show.</returns>
    public static InstanceInfo Merge(InstanceInfo platform, ApplicationReport? application, bool sidecarLost)
    {
        if (application is null)
        {
            return platform;
        }

        var serviceName = string.IsNullOrWhiteSpace(application.ServiceName) ? platform.ServiceName : application.ServiceName;

        if (sidecarLost)
        {
            // The pod exists but nothing can be said about the application: unhealthy until the sidecar returns.
            return platform with
            {
                ServiceName = serviceName,
                Version = application.Version,
                Health = HealthState.Unhealthy,
                Checks = [new HealthCheckResult("Plover sidecar", HealthState.Unhealthy, "The sidecar is not connected.")],
            };
        }

        return platform with
        {
            ServiceName = serviceName,
            Version = application.Version,
            Health = platform.Health.Worst(application.Health),
            // What the platform knows about the pod, such as a warning of the cluster or a rollout, stays beside what the application says about itself.
            Checks = [.. platform.Checks ?? [], .. application.Checks],
            // The use of the pod that the cluster reports stays beside the metrics of the application.
            Metrics = platform.Metrics is { Count: > 0 } ? [.. platform.Metrics, .. application.Metrics ?? []] : application.Metrics,
            Edges = application.Edges,
            Packages = application.Packages,
        };
    }
}
