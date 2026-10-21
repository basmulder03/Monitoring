using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Agent.Features.Configuration;

/// <summary>Registers the configuration slice.</summary>
public static class ConfigurationSlice
{
    /// <summary>Adds the holder of the configuration the controller hands to this agent.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAgentConfiguration(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IAgentConfiguration, AgentConfiguration>();
        return services;
    }
}
