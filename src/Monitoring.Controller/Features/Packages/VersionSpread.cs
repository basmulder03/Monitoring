namespace Monitoring.Controller.Features.Packages;

/// <summary>How far apart the versions of a package that must be aligned may be.</summary>
public enum VersionSpread : byte
{
    /// <summary>Every service uses the same version.</summary>
    Exact,

    /// <summary>Versions may differ in the patch number only.</summary>
    Minor,

    /// <summary>Versions may differ in the minor and patch numbers, but not in the major number.</summary>
    Major,
}
