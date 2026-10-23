using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Agent.Features.Configuration;

/// <summary>Registers the configuration slice.</summary>
public static class ConfigurationSlice
{
    /// <summary>Adds the holder of the configuration the controller hands to this agent.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally sets the local overrides, which count only for the settings the central configuration allows to be overridden.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An override is below 1 second.</exception>
    public static IServiceCollection AddAgentConfiguration(this IServiceCollection services, Action<AgentConfigurationOptions>? configure = null)
    {
        var options = new AgentConfigurationOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PullSeconds ?? 1, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.EdgeFlushSeconds ?? 1, 1);

        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton<IAgentConfiguration, AgentConfiguration>();
        return services;
    }
}
