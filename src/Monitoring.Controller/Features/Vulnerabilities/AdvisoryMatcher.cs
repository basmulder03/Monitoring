namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Decides whether a version of a package is affected by an advisory.</summary>
internal static class AdvisoryMatcher
{
    private const string Ecosystem = "NuGet";

    /// <summary>Checks one version of one package against one advisory.</summary>
    /// <param name="advisory">The advisory.</param>
    /// <param name="package">The package id; case does not matter.</param>
    /// <param name="version">The version in use.</param>
    /// <returns>Whether it is affected, and the first version without the problem when the advisory names one.</returns>
    public static (bool Affected, string? FixedIn) Match(OsvAdvisory advisory, string package, string version)
    {
        foreach (var affected in advisory.Affected ?? [])
        {
            if (!string.Equals(affected.Package?.Ecosystem, Ecosystem, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(affected.Package?.Name, package, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (affected.Versions?.Contains(version, StringComparer.OrdinalIgnoreCase) == true)
            {
                return (true, FixAfter(affected, version));
            }

            if (affected.Ranges?.Any(range => InRange(range, version)) == true)
            {
                return (true, FixAfter(affected, version));
            }
        }

        return (false, null);
    }

    /// <summary>Walks the events of a range in version order: an introduced version switches "affected" on, a fix or the last affected version switches it off.</summary>
    private static bool InRange(OsvRange range, string version)
    {
        if (range.Type is not null && !range.Type.Equals("ECOSYSTEM", StringComparison.OrdinalIgnoreCase) && !range.Type.Equals("SEMVER", StringComparison.OrdinalIgnoreCase))
        {
            // Ranges of commits (GIT) say nothing about published versions.
            return false;
        }

        var affected = false;
        foreach (var change in (range.Events ?? []).OrderBy(Position, VersionOrder.Instance))
        {
            if (change.Introduced is { } introduced && VersionOrder.Instance.Compare(version, introduced) >= 0)
            {
                affected = true;
            }
            else if (change.Fixed is { } fix && VersionOrder.Instance.Compare(version, fix) >= 0)
            {
                affected = false;
            }
            else if (change.LastAffected is { } last && VersionOrder.Instance.Compare(version, last) > 0)
            {
                affected = false;
            }
        }

        return affected;
    }

    private static string Position(OsvEvent change) => change.Introduced ?? change.Fixed ?? change.LastAffected ?? "0";

    private static string? FixAfter(OsvAffected affected, string version) =>
        (affected.Ranges ?? [])
            .SelectMany(range => range.Events ?? [])
            .Select(change => change.Fixed)
            .OfType<string>()
            .Where(fix => VersionOrder.Instance.Compare(fix, version) > 0)
            .OrderBy(fix => fix, VersionOrder.Instance)
            .FirstOrDefault();
}
