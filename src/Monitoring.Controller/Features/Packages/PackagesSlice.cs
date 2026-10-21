using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Packages;

/// <summary>Registers the packages slice.</summary>
public static class PackagesSlice
{
    /// <summary>Adds the inventory of the packages the services are built from. It reads from the registry, so <c>AddRegistry</c> must be called as well; <c>MapPackages</c> adds the endpoints.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPackages(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPackageInventory, PackageInventory>();
        return services;
    }
}
