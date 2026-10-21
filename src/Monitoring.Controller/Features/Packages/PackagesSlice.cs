using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Packages;

/// <summary>Registers the packages slice.</summary>
public static class PackagesSlice
{
    /// <summary>Adds the inventory of the packages the services are built from. It reads from the registry, so <c>AddRegistry</c> must be called as well; <c>MapPackages</c> adds the endpoints.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="PackagesOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">An alignment rule names no package.</exception>
    public static IServiceCollection AddPackages(this IServiceCollection services, Action<PackagesOptions>? configure = null)
    {
        var options = new PackagesOptions();
        configure?.Invoke(options);
        foreach (var rule in options.Aligned.Where(rule => string.IsNullOrWhiteSpace(rule.Package)))
        {
            throw new ArgumentException("An alignment rule needs the name of a package, or a prefix ending in '*'.", nameof(configure));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<IPackageInventory, PackageInventory>();
        return services;
    }
}
