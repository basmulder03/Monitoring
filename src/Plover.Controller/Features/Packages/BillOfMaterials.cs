using Plover.Protocol.Reports;

namespace Plover.Controller.Features.Packages;

/// <summary>Makes bills of materials in the CycloneDX format from what the services reported.</summary>
internal static class BillOfMaterials
{
    /// <summary>The bill of materials of one service.</summary>
    /// <param name="service">What the service is built from.</param>
    /// <param name="now">The time to put in the document.</param>
    /// <returns>The document.</returns>
    public static CycloneDxBom ForService(ServicePackages service, DateTimeOffset now) =>
        Make(
            new CycloneDxComponent("application", service.Service, service.Version, $"service:{service.Scope}/{service.Service}"),
            service.Packages,
            now);

    /// <summary>The bill of materials of everything: each distinct package and version once.</summary>
    /// <param name="services">What every service is built from.</param>
    /// <param name="now">The time to put in the document.</param>
    /// <returns>The document.</returns>
    public static CycloneDxBom ForLandscape(IReadOnlyList<ServicePackages> services, DateTimeOffset now) =>
        Make(
            new CycloneDxComponent("platform", "landscape", null, "landscape"),
            [.. services.SelectMany(service => service.Packages).DistinctBy(package => (package.Name.ToLowerInvariant(), package.Version))],
            now);

    private static CycloneDxBom Make(CycloneDxComponent subject, IEnumerable<PackageReport> packages, DateTimeOffset now) =>
        new(
            "CycloneDX",
            "1.5",
            1,
            $"urn:uuid:{Guid.NewGuid()}",
            new CycloneDxMetadata(now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), subject),
            [.. packages
                .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Version, VersionOrder.Instance)
                .Select(package => new CycloneDxComponent(
                    "library",
                    package.Name,
                    package.Version,
                    $"pkg:nuget/{package.Name}@{package.Version}",
                    $"pkg:nuget/{package.Name}@{package.Version}",
                    "required"))]);
}
