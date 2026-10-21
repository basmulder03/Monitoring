using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>Registers the sidecar connections slice.</summary>
public static class SidecarConnectionsSlice
{
    /// <summary>
    /// Adds the listener for sidecar connections and the background service that runs it. It reports into the scope state,
    /// so <c>AddScopeState</c> must be called as well.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="SidecarConnectionsOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The listen address is not an IP address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port, an interval or the frame limit is out of range.</exception>
    public static IServiceCollection AddSidecarConnections(
        this IServiceCollection services, Action<SidecarConnectionsOptions>? configure = null)
    {
        var options = new SidecarConnectionsOptions();
        configure?.Invoke(options);
        if (!IPAddress.TryParse(options.ListenAddress, out _))
        {
            throw new ArgumentException($"'{options.ListenAddress}' is not an IP address.", nameof(configure));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HelloTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.HeartbeatInterval, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MissedHeartbeats, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HealthPullInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MetricsPullInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxFrameBytes, 0);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<SidecarConnectionHandler>();
        services.AddSingleton<ISidecarListener, SidecarListener>();
        services.AddHostedService<SidecarListenerService>();
        return services;
    }
}
