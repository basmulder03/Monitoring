using Microsoft.Extensions.DependencyInjection.Extensions;
using Plover.Client;
using Microsoft.Extensions.Hosting;
using Plover.Client.Features.Certificates;
using Plover.Client.Features.Health;
using Plover.Client.Features.Info;
using Plover.Client.Features.LocalServer;
using Plover.Client.Features.Metrics;
using Plover.Client.Features.Queues;
using Plover.Client.Features.Packages;
using Plover.Client.Features.Tracing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Adds the Plover client to an application.</summary>
public static class PloverExtensions
{
    /// <summary>
    /// Makes the application visible to the monitoring system: it listens for the sidecar and answers with its name, version
    /// and health checks. Register health checks the usual way with <c>AddHealthChecks()</c>; tag a check with
    /// <see cref="PloverTags.NonCritical"/> to show a failure as Degraded instead of Unhealthy.
    /// </summary>
    /// <remarks>It never stops the application: when it cannot listen, for example without a sidecar, it logs a warning and stays off.</remarks>
    /// <param name="services">The service collection of the application.</param>
    /// <param name="configure">Optionally changes the <see cref="PloverOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPlover(this IServiceCollection services, Action<PloverOptions>? configure = null)
    {
        var options = new PloverOptions();
        configure?.Invoke(options);

        // Health checks read options; adding them is safe when the application already has them. Logging comes from the application's host.
        services.AddOptions();
        services.AddHealthChecks();
        services.AddSingleton(options);
        services.TryAddSingleton<InstanceDescriber>();
        services.TryAddSingleton<HealthReporter>();
        services.AddSingleton<IPullSource, InfoPullSource>();
        services.AddSingleton<IPullSource, HealthPullSource>();
        services.TryAddSingleton(TimeProvider.System);
        if (options.Certificates.Count > 0)
        {
            services.TryAddSingleton<CertificateMeter>();
            services.AddHostedService<CertificateMeterService>();
        }

        if (options.Queues.Count > 0)
        {
            services.TryAddSingleton<QueueMeter>();
            services.AddHostedService<QueueMeterService>();
        }

        services.TryAddSingleton<MetricsCollector>();
        services.AddHostedService(provider => provider.GetRequiredService<MetricsCollector>());
        services.AddSingleton<IPullSource, MetricsPullSource>();
        services.TryAddSingleton(new EdgeCounters(options.MaxEdges));
        services.TryAddSingleton(new SpanBuffer(options.SpanBufferSize));
        services.TryAddSingleton<TraceCollector>();
        services.AddHostedService(provider => provider.GetRequiredService<TraceCollector>());
        services.AddSingleton<IPullSource, EdgesPullSource>();
        services.AddSingleton<IPullSource, SpansPullSource>();
        services.AddSingleton<IPullSource, PackagesPullSource>();
        services.TryAddSingleton<PullHandler>();
        services.AddHostedService<LocalServer>();
        services.AddHostedService<AgentPipeLink>();
        return services;
    }
}
