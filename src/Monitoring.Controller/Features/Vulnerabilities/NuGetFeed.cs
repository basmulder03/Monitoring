using System.Text.Json.Serialization;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>The resources a NuGet source lists, or the pages of its vulnerability feed; only what is read is declared.</summary>
/// <param name="Name"><c>base</c> or <c>update</c>.</param>
/// <param name="Id">Where the page is.</param>
internal sealed record NuGetPage([property: JsonPropertyName("@name")] string? Name, [property: JsonPropertyName("@id")] string? Id);

/// <summary>One vulnerability of a package in the NuGet feed.</summary>
/// <param name="Url">The advisory, for example a GitHub advisory.</param>
/// <param name="Severity">0 low, 1 moderate, 2 high, 3 critical.</param>
/// <param name="Versions">The affected versions, in the interval notation of NuGet.</param>
internal sealed record NuGetEntry(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("severity")] int Severity,
    [property: JsonPropertyName("versions")] string? Versions);

/// <summary>Source-generated JSON for the NuGet vulnerability feed.</summary>
[JsonSerializable(typeof(List<NuGetPage>))]
[JsonSerializable(typeof(Dictionary<string, List<NuGetEntry>>))]
internal sealed partial class NuGetFeedJsonContext : JsonSerializerContext;

/// <summary>Turns what the NuGet vulnerability feed says into advisories in the OSV model, so the same matching serves both.</summary>
internal static class NuGetFeed
{
    private static readonly string[] Severities = ["LOW", "MODERATE", "HIGH", "CRITICAL"];

    /// <summary>
    /// Combines the pages: a package that a later page lists replaces what an earlier page said about it, because <c>update</c> holds everything that changed since <c>base</c>.
    /// </summary>
    /// <param name="pages">The pages in the order <c>base</c>, <c>update</c>.</param>
    /// <returns>The advisories, one per advisory url.</returns>
    public static IReadOnlyList<OsvAdvisory> ToAdvisories(IEnumerable<Dictionary<string, List<NuGetEntry>>> pages)
    {
        var byPackage = new Dictionary<string, List<NuGetEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            foreach (var (package, entries) in page)
            {
                byPackage[package] = entries;
            }
        }

        var advisories = new Dictionary<string, OsvAdvisory>(StringComparer.Ordinal);
        foreach (var (package, entries) in byPackage)
        {
            foreach (var entry in entries)
            {
                if (entry.Url is not { Length: > 0 } url || !TryParseRange(entry.Versions, out var affected))
                {
                    continue;
                }

                var id = url.TrimEnd('/')[(url.TrimEnd('/').LastIndexOf('/') + 1)..];
                var one = affected with { Package = new OsvPackage("NuGet", package) };
                advisories[id] = advisories.TryGetValue(id, out var known)
                    ? known with { Affected = [.. known.Affected ?? [], one] }
                    : new OsvAdvisory(id, url, null, [one], new OsvDatabaseSpecific(Severities[Math.Clamp(entry.Severity, 0, Severities.Length - 1)]));
            }
        }

        return [.. advisories.Values];
    }

    /// <summary>
    /// Reads the interval notation of NuGet, such as <c>(, 5.8.4)</c>, <c>[2.1.0, 2.1.13)</c>, <c>[2.2.0, 2.2.0]</c> or <c>[1.0.0]</c>, as OSV ranges.
    /// shortcut: an exclusive lower bound counts as inclusive, so exactly that version is reported although it is not affected; the feed does not use one today.
    /// </summary>
    /// <param name="notation">The notation.</param>
    /// <param name="affected">The affected versions, without the package.</param>
    /// <returns>Whether the notation could be read.</returns>
    public static bool TryParseRange(string? notation, out OsvAffected affected)
    {
        affected = new OsvAffected();
        var text = notation?.Trim();
        if (text is not { Length: >= 3 } || text[0] is not ('[' or '(') || text[^1] is not (']' or ')'))
        {
            return false;
        }

        var inner = text[1..^1];
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            affected = new OsvAffected(Versions: [inner.Trim()]);
            return inner.Trim().Length > 0;
        }

        var lower = inner[..comma].Trim();
        var upper = inner[(comma + 1)..].Trim();
        var events = new List<OsvEvent> { new(Introduced: lower.Length == 0 ? "0" : lower) };
        if (upper.Length > 0)
        {
            events.Add(text[^1] == ']' ? new OsvEvent(LastAffected: upper) : new OsvEvent(Fixed: upper));
        }

        affected = new OsvAffected(Ranges: [new OsvRange("ECOSYSTEM", events)]);
        return true;
    }
}
