using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plover.Controller.Features.Packages;

/// <summary>Maps the read API of the package inventory.</summary>
public static class PackagesEndpoints
{
    private const string CycloneDxMediaType = "application/vnd.cyclonedx+json";

    /// <summary>
    /// Maps <c>GET /api/packages</c>, <c>GET /api/packages/{name}</c>, <c>GET /api/sbom</c> (the whole landscape as CycloneDX) and
    /// <c>GET /api/sbom/{scope}/{service}</c> (one service as CycloneDX).
    /// </summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapPackages(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/packages", (IPackageInventory inventory) =>
            Results.Json(inventory.GetCatalog(), PackagesJsonContext.Default.PackageCatalog));

        endpoints.MapGet("/api/packages/{name}", (string name, IPackageInventory inventory) =>
            inventory.Find(name) is { } entry ? Results.Json(entry, PackagesJsonContext.Default.PackageEntry) : Results.NotFound());

        endpoints.MapGet("/api/sbom", (IPackageInventory inventory, TimeProvider time) =>
            Results.Json(BillOfMaterials.ForLandscape(inventory.GetAll(), time.GetUtcNow()), PackagesJsonContext.Default.CycloneDxBom, CycloneDxMediaType));

        endpoints.MapGet("/api/sbom/{scope}/{service}", (string scope, string service, IPackageInventory inventory, TimeProvider time) =>
            inventory.ForService(scope, service) is { } packages
                ? Results.Json(BillOfMaterials.ForService(packages, time.GetUtcNow()), PackagesJsonContext.Default.CycloneDxBom, CycloneDxMediaType)
                : Results.NotFound());

        return endpoints;
    }
}
