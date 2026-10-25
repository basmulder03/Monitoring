namespace Plover.Controller.Features.Packages;

/// <summary>Settings of the packages slice.</summary>
public sealed class PackagesOptions
{
    /// <summary>
    /// The packages that must be used in the same version, and how strictly. Packages that no rule covers are only reported;
    /// different versions of them are not a problem. The first rule that covers a package decides.
    /// </summary>
    public List<AlignmentRule> Aligned { get; set; } = [];
}
