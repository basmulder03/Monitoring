using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Registers the vulnerabilities slice.</summary>
public static class VulnerabilitiesSlice
{
    /// <summary>
    /// Adds the check of the packages in use against advisories. It reads the inventory, so <c>AddPackages</c> must be called as well;
    /// <c>MapVulnerabilities</c> adds the endpoint.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="VulnerabilitiesOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The poll or fetch interval or the fetch timeout is not positive, or the download limit is.</exception>
    /// <exception cref="ArgumentException">A source is of an unknown kind or its address is not an absolute HTTPS address.</exception>
    public static IServiceCollection AddVulnerabilities(this IServiceCollection services, Action<VulnerabilitiesOptions>? configure = null)
    {
        var options = new VulnerabilitiesOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.FetchInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.FetchTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxDownloadBytes, 0);
        foreach (var source in options.Sources)
        {
            if (source.Kind is not (AdvisorySource.NuGetKind or AdvisorySource.OsvKind))
            {
                throw new ArgumentException($"The kind of source '{source.Kind}' is not known; use '{AdvisorySource.NuGetKind}' or '{AdvisorySource.OsvKind}'.", nameof(configure));
            }

            if (!source.Url.IsAbsoluteUri || source.Url.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException($"The address of a source must be an absolute HTTPS address, not '{source.Url}'.", nameof(configure));
            }
        }

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<AdvisoryStore>();
        services.AddSingleton<IVulnerabilities, VulnerabilityCheck>();
        services.AddHostedService<AdvisoryReloadService>();
        if (options.Sources.Count > 0)
        {
            // The controller asks for these addresses and nothing else; certificates are checked as usual.
            services.AddSingleton(_ => new AdvisoryFetcher(
                new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan, DefaultRequestHeaders = { { "User-Agent", "Monitoring-controller" } } },
                options));
            services.AddSingleton<AdvisoryFetchService>();
            services.AddHostedService(provider => provider.GetRequiredService<AdvisoryFetchService>());
        }

        return services;
    }
}
