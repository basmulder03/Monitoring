using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Agent.Features.SelfMonitoring;

/// <summary>Registers the self-monitoring slice.</summary>
public static class SelfMonitoringSlice
{
    /// <summary>Adds what the agent reports about itself. It reports through the scope state, so <c>AddScopeState</c> must be called as well; add it before the slices that count into it.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="SelfMonitoringOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="ArgumentException">The pod name is blank.</exception>
    public static IServiceCollection AddSelfMonitoring(this IServiceCollection services, Action<SelfMonitoringOptions>? configure = null)
    {
        var options = new SelfMonitoringOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Interval, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PodName);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<AgentTelemetry>();
        services.AddSingleton<IAgentTelemetry>(provider => provider.GetRequiredService<AgentTelemetry>());
        services.AddHostedService<SelfReportService>();
        return services;
    }
}
