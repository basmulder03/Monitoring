namespace Plover.Controller.Features.Packages;

/// <summary>A package, or a family of packages, that the services of the landscape are expected to use in the same version.</summary>
/// <param name="Package">A package id, or a prefix ending in <c>*</c> such as <c>Microsoft.Extensions.*</c>; case does not matter.</param>
/// <param name="Spread">How far apart the versions may be.</param>
/// <param name="Description">Why the rule exists, in the words of whoever wrote the configuration; <see langword="null"/> when there is none. Shown on the rules page only.</param>
public sealed record AlignmentRule(string Package, VersionSpread Spread, string? Description = null)
{
    /// <summary>Tells whether the rule applies to a package.</summary>
    /// <param name="name">The package id.</param>
    /// <returns><see langword="true"/> when the id is the package or starts with the prefix.</returns>
    public bool Covers(string name) =>
        Package.EndsWith('*')
            ? name.StartsWith(Package[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(name, Package, StringComparison.OrdinalIgnoreCase);

    /// <summary>Describes the rule in words, for the page.</summary>
    /// <returns>For example <c>Microsoft.Extensions.* must have the same major version</c>.</returns>
    public override string ToString() => $"{Package} must {Spread switch
    {
        VersionSpread.Exact => "be the same version everywhere",
        VersionSpread.Minor => "have the same major and minor version",
        _ => "have the same major version",
    }}";
}
