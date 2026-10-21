namespace Monitoring.Controller.Features.Packages;

/// <summary>Every package used anywhere in the landscape.</summary>
/// <param name="Packages">The packages, ordered by name.</param>
/// <param name="Services">How many services have reported what they are built from.</param>
/// <param name="ServicesWithoutList">How many services have not, for example because they were built without the monitoring client's build targets, so what is missing from the catalog is known.</param>
public sealed record PackageCatalog(IReadOnlyList<PackageEntry> Packages, int Services, int ServicesWithoutList);
