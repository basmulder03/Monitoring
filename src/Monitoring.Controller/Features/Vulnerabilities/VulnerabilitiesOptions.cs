namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Settings of the vulnerabilities slice.</summary>
public sealed class VulnerabilitiesOptions
{
    /// <summary>
    /// Where the advisories are: one JSON file (a single advisory or a list) or a folder of such files, in the OSV format, for example an export of the
    /// GitHub Advisory Database for NuGet. Without it nothing is checked. The controller does not fetch anything itself, so it can run in a closed network;
    /// a scheduled job keeps the file current.
    /// </summary>
    public string? Advisories { get; set; }

    /// <summary>How often the advisories are looked at for a change.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
}
