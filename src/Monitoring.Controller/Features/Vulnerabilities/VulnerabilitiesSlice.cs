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
    /// <exception cref="ArgumentOutOfRangeException">The poll interval is not positive.</exception>
    public static IServiceCollection AddVulnerabilities(this IServiceCollection services, Action<VulnerabilitiesOptions>? configure = null)
    {
        var options = new VulnerabilitiesOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<AdvisoryStore>();
        services.AddSingleton<IVulnerabilities, VulnerabilityCheck>();
        services.AddHostedService<AdvisoryReloadService>();
        return services;
    }
}
