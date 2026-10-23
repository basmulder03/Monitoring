using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Sidecar.Features.SelfMonitoring;

/// <summary>Registers the self-monitoring slice.</summary>
public static class SelfMonitoringSlice
{
    /// <summary>Adds the measurements the sidecar reports about itself. Add it before <c>AddAgentLink</c> so the link includes them.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSelfMonitoring(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISelfMetrics, SelfMetrics>();
        return services;
    }
}
