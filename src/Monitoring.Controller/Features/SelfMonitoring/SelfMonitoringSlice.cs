using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.SelfMonitoring;

/// <summary>Registers the self-monitoring slice.</summary>
public static class SelfMonitoringSlice
{
    /// <summary>Adds the telemetry of this replica. It reads from the registry and liveness, so <c>AddRegistry</c> and <c>AddLiveness</c> must be called as well; <c>MapSelfMonitoring</c> adds the endpoint.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSelfMonitoring(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SystemTelemetry>();
        services.AddSingleton<ISystemTelemetry>(provider => provider.GetRequiredService<SystemTelemetry>());
        return services;
    }
}
