namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Settings of the vulnerabilities slice.</summary>
public sealed class VulnerabilitiesOptions
{
    /// <summary>The NuGet feed of vulnerabilities, which is built from the GitHub Advisory Database.</summary>
    public static readonly AdvisorySource NuGetFeed = new(AdvisorySource.NuGetKind, new Uri("https://api.nuget.org/v3/vulnerabilities/index.json"));

    /// <summary>The OSV export of the advisories about NuGet packages.</summary>
    public static readonly AdvisorySource OsvNuGet = new(AdvisorySource.OsvKind, new Uri("https://osv-vulnerabilities.storage.googleapis.com/NuGet/all.zip"));

    /// <summary>
    /// Where the advisories are: one JSON file (a single advisory or a list) or a folder of such files, in the OSV format, for example an export of the
    /// GitHub Advisory Database for NuGet. Without it, and without <see cref="Sources"/>, nothing is checked. The controller fetches nothing unless
    /// <see cref="Sources"/> says so, so it can run in a closed network; a scheduled job can keep the file current instead.
    /// </summary>
    public string? Advisories { get; set; }

    /// <summary>How often the advisories file is looked at for a change.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Public sources to download advisories from, which is optional and empty by default. Only these addresses are requested, with a plain HTTPS GET: nothing
    /// about the packages in use is sent. What is fetched is added to what <see cref="Advisories"/> holds.
    /// </summary>
    public List<AdvisorySource> Sources { get; set; } = [];

    /// <summary>How often the sources are fetched again.</summary>
    public TimeSpan FetchInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How long fetching one source may take before it is given up on until the next time.</summary>
    public TimeSpan FetchTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The most a source may send, before and after decompression, which bounds the memory a download can claim.</summary>
    public long MaxDownloadBytes { get; set; } = 64L * 1024 * 1024;
}

/// <summary>A public place advisories are downloaded from.</summary>
/// <param name="Kind"><c>nuget</c>: the vulnerability feed of a NuGet source, given by the address of its <c>index.json</c> of vulnerabilities. <c>osv</c>: an OSV file, a list of advisories in one file, or a zip of such files.</param>
/// <param name="Url">The address, which must be HTTPS.</param>
public sealed record AdvisorySource(string Kind, Uri Url)
{
    /// <summary>The kind for the NuGet vulnerability feed.</summary>
    public const string NuGetKind = "nuget";

    /// <summary>The kind for OSV files.</summary>
    public const string OsvKind = "osv";
}
