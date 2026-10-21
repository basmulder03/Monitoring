namespace Monitoring.Controller.Features.Packages;

/// <summary>A package, with every version of it that is in use somewhere.</summary>
/// <param name="Name">The package id.</param>
/// <param name="Versions">The versions in use, newest first. More than one means the landscape does not agree on it.</param>
/// <param name="Violation">Which alignment rule the versions in use break, in words; <see langword="null"/> when no rule covers the package or the versions are within it.</param>
public sealed record PackageEntry(string Name, IReadOnlyList<PackageVersion> Versions, string? Violation = null);
