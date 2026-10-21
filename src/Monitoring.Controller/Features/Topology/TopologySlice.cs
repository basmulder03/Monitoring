using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Topology;

/// <summary>Registers the topology slice.</summary>
public static class TopologySlice
{
    /// <summary>Adds the dependency graph and the background service that keeps it up to date. It reads from the registry, so <c>AddRegistry</c> must be called as well.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="TopologyOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An interval is not positive.</exception>
    public static IServiceCollection AddTopology(this IServiceCollection services, Action<TopologyOptions>? configure = null)
    {
        var options = new TopologyOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SampleInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.EdgeFadeAfter, TimeSpan.Zero);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<TopologyTracker>();
        services.AddSingleton<ITopology>(provider => provider.GetRequiredService<TopologyTracker>());
        services.AddHostedService<TopologySampleService>();
        return services;
    }
}
