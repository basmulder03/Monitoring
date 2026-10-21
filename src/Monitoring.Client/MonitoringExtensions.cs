using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Client;
using Microsoft.Extensions.Hosting;
using Monitoring.Client.Features.Health;
using Monitoring.Client.Features.Info;
using Monitoring.Client.Features.LocalServer;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds the monitoring client to an application.</summary>
public static class MonitoringExtensions
{
    /// <summary>
    /// Makes the application visible to the monitoring system: it listens for the sidecar and answers with its name, version
    /// and health checks. Register health checks the usual way with <c>AddHealthChecks()</c>; tag a check with
    /// <see cref="MonitoringTags.NonCritical"/> to show a failure as Degraded instead of Unhealthy.
    /// </summary>
    /// <remarks>It never stops the application: when it cannot listen, for example without a sidecar, it logs a warning and stays off.</remarks>
    /// <param name="services">The service collection of the application.</param>
    /// <param name="configure">Optionally changes the <see cref="MonitoringOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMonitoring(this IServiceCollection services, Action<MonitoringOptions>? configure = null)
    {
        var options = new MonitoringOptions();
        configure?.Invoke(options);

        // Health checks read options; adding them is safe when the application already has them. Logging comes from the application's host.
        services.AddOptions();
        services.AddHealthChecks();
        services.AddSingleton(options);
        services.TryAddSingleton<InstanceDescriber>();
        services.TryAddSingleton<HealthReporter>();
        services.AddSingleton<IPullSource, InfoPullSource>();
        services.AddSingleton<IPullSource, HealthPullSource>();
        services.TryAddSingleton<PullHandler>();
        services.AddHostedService<LocalServer>();
        return services;
    }
}
