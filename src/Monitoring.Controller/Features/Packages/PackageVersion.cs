namespace Monitoring.Controller.Features.Packages;

/// <summary>One version of a package and who uses it.</summary>
/// <param name="Version">The version as restored and built into the applications.</param>
/// <param name="UsedBy">The services that use it, ordered by scope and service.</param>
public sealed record PackageVersion(string Version, IReadOnlyList<PackageUsage> UsedBy);
