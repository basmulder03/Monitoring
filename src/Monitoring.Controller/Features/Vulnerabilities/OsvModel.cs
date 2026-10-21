namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>One advisory in the OSV format, reduced to what is needed to match packages. Names follow the format, so a file from any OSV source reads.</summary>
/// <param name="Id">The id of the advisory, such as <c>GHSA-xxxx-xxxx-xxxx</c>.</param>
/// <param name="Summary">One line about the problem.</param>
/// <param name="Aliases">Other ids of the same problem, such as a CVE number.</param>
/// <param name="Affected">The packages and versions the advisory is about.</param>
/// <param name="DatabaseSpecific">Extra data of the database the advisory comes from, which for GitHub advisories holds the severity.</param>
internal sealed record OsvAdvisory(
    string Id,
    string? Summary = null,
    List<string>? Aliases = null,
    List<OsvAffected>? Affected = null,
    OsvDatabaseSpecific? DatabaseSpecific = null);

/// <summary>The packages and versions an advisory is about.</summary>
/// <param name="Package">The package.</param>
/// <param name="Ranges">Ranges of versions that are affected.</param>
/// <param name="Versions">Single versions that are affected.</param>
internal sealed record OsvAffected(OsvPackage? Package = null, List<OsvRange>? Ranges = null, List<string>? Versions = null);

/// <summary>A package in an ecosystem.</summary>
/// <param name="Ecosystem">The ecosystem, such as <c>NuGet</c>.</param>
/// <param name="Name">The package id.</param>
internal sealed record OsvPackage(string? Ecosystem = null, string? Name = null);

/// <summary>A range of affected versions, described by the events that start and end it.</summary>
/// <param name="Type">How versions are compared; <c>ECOSYSTEM</c> and <c>SEMVER</c> are understood.</param>
/// <param name="Events">What starts and ends affected stretches, in order.</param>
internal sealed record OsvRange(string? Type = null, List<OsvEvent>? Events = null);

/// <summary>A version where something changes. One of the three is set.</summary>
/// <param name="Introduced">The first affected version; <c>0</c> means from the start.</param>
/// <param name="Fixed">The first version that is no longer affected.</param>
/// <param name="LastAffected">The last affected version, for problems without a fix.</param>
internal sealed record OsvEvent(string? Introduced = null, string? Fixed = null, string? LastAffected = null);

/// <summary>Extra data of the database an advisory comes from.</summary>
/// <param name="Severity">For GitHub advisories: LOW, MODERATE, HIGH or CRITICAL.</param>
internal sealed record OsvDatabaseSpecific(string? Severity = null);
