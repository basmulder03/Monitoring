using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Sidecar.Features.Probing;

/// <summary>Registers the probing slice.</summary>
public static class ProbingSlice
{
    /// <summary>Adds the regular probing of the application. It asks through the library link, so <c>AddLibraryLink</c> must be called as well.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="ProbeOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    public static IServiceCollection AddProbing(this IServiceCollection services, Action<ProbeOptions>? configure = null)
    {
        var options = new ProbeOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Interval, TimeSpan.Zero);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<ProbeService>();
        services.AddSingleton<IProbeResults>(provider => provider.GetRequiredService<ProbeService>());
        services.AddHostedService(provider => provider.GetRequiredService<ProbeService>());
        return services;
    }
}
