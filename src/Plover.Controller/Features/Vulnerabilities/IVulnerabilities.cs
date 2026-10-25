namespace Plover.Controller.Features.Vulnerabilities;

/// <summary>The packages in use that known advisories say are vulnerable.</summary>
public interface IVulnerabilities
{
    /// <summary>Checks the packages in use against the advisories that are loaded.</summary>
    /// <returns>The findings and how many advisories were checked against.</returns>
    VulnerabilityReport Check();
}
