using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Monitoring.Common.Prometheus;

/// <summary>Registers the optional Prometheus export of an executable.</summary>
public static class PrometheusRegistration
{
    /// <summary>Adds the export when a port is configured; does nothing otherwise.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="PrometheusOptions"/>.</param>
    /// <param name="source">Creates the function that reads the executable's own values on every scrape.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The port is out of range.</exception>
    /// <exception cref="ArgumentException">The address is not an IP address.</exception>
    public static IServiceCollection AddPrometheus(
        this IServiceCollection services, Action<PrometheusOptions> configure, Func<IServiceProvider, Func<IEnumerable<PrometheusSample>>> source)
    {
        var options = new PrometheusOptions();
        configure(options);
        if (options.Port == 0)
        {
            return services;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, 65535);
        if (!IPAddress.TryParse(options.Address, out _))
        {
            throw new ArgumentException($"'{options.Address}' is not an IP address.", nameof(configure));
        }

        services.AddHostedService(provider => new PrometheusService(options, source(provider), provider.GetRequiredService<ILogger<PrometheusService>>()));
        return services;
    }
}
