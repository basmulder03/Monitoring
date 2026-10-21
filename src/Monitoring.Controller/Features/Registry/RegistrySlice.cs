using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Registry;

/// <summary>Registers the registry slice.</summary>
public static class RegistrySlice
{
    /// <summary>Adds the in-memory scope registry.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="RegistryOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The configured retention is negative.</exception>
    public static IServiceCollection AddRegistry(this IServiceCollection services, Action<RegistryOptions>? configure = null)
    {
        var options = new RegistryOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.GoneRetention, TimeSpan.Zero);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<IScopeRegistry, ScopeRegistry>();
        return services;
    }
}
