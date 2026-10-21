using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Controller.Features.Health;

/// <summary>Registers the health slice.</summary>
public static class HealthSlice
{
    /// <summary>Adds the health roll-up. It reads from the registry and the liveness tracking, so <c>AddRegistry</c> and <c>AddLiveness</c> must be called as well.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        services.AddSingleton<IHealthRollup, HealthRollup>();
        return services;
    }
}
