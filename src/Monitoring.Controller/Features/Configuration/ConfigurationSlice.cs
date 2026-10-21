using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Configuration;

/// <summary>Registers the configuration slice.</summary>
public static class ConfigurationSlice
{
    /// <summary>Adds the central configuration and the background service that keeps it current.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="ConfigurationOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The poll interval is not positive.</exception>
    public static IServiceCollection AddConfiguration(this IServiceCollection services, Action<ConfigurationOptions>? configure = null)
    {
        var options = new ConfigurationOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<EffectiveConfiguration>();
        services.AddSingleton<IEffectiveConfiguration>(provider => provider.GetRequiredService<EffectiveConfiguration>());
        services.AddSingleton<IAppliedConfigurations, AppliedConfigurations>();
        services.AddHostedService<ConfigurationReloadService>();
        return services;
    }
}
