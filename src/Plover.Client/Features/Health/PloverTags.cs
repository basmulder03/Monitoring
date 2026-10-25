namespace Plover.Client.Features.Health;

/// <summary>Tags that change how the monitoring system reads an application's health checks.</summary>
public static class PloverTags
{
    /// <summary>
    /// Marks a health check as non-critical: when it fails the application is shown as Degraded instead of Unhealthy.
    /// Use it for things the application can live without for a while, such as a cache or a recommendation service.
    /// </summary>
    public const string NonCritical = "monitoring:non-critical";
}
