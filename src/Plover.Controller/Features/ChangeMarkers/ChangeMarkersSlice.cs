using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Plover.Controller.Features.ChangeMarkers;

/// <summary>Registers the change markers slice.</summary>
public static class ChangeMarkersSlice
{
    /// <summary>Adds the markers and the background service that keeps them up to date. It reads from the registry, so <c>AddRegistry</c> must be called as well; <c>MapChangeMarkers</c> adds the endpoint.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="ChangeMarkersOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval or the number of markers is not positive.</exception>
    public static IServiceCollection AddChangeMarkers(this IServiceCollection services, Action<ChangeMarkersOptions>? configure = null)
    {
        var options = new ChangeMarkersOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SampleInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxMarkers, 1);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<ChangeTracker>();
        services.AddSingleton<IChangeMarkers>(provider => provider.GetRequiredService<ChangeTracker>());
        services.AddHostedService<ChangeSampleService>();
        return services;
    }
}
