namespace Plover.Controller.Features.Packages;

/// <summary>What the services of the landscape are built from, as their applications reported it.</summary>
public interface IPackageInventory
{
    /// <summary>Gets every package in use, with its versions and who uses them.</summary>
    /// <returns>The catalog.</returns>
    PackageCatalog GetCatalog();

    /// <summary>Gets one package.</summary>
    /// <param name="name">The package id; case does not matter.</param>
    /// <returns>The package, or <see langword="null"/> when no service uses it.</returns>
    PackageEntry? Find(string name);

    /// <summary>Gets what one service is built from.</summary>
    /// <param name="scope">The scope the service runs in.</param>
    /// <param name="service">The name of the service.</param>
    /// <returns>The packages, or <see langword="null"/> when the service has not reported any.</returns>
    ServicePackages? ForService(string scope, string service);

    /// <summary>Gets what every service reported, for exports that cover the landscape.</summary>
    /// <returns>One entry per service that has reported, ordered by scope and service.</returns>
    IReadOnlyList<ServicePackages> GetAll();
}
