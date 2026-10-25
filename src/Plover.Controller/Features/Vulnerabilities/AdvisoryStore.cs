using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Vulnerabilities;

/// <summary>
/// Holds the advisories read from the file or folder, and reads them again when something there changed, together with what was downloaded from public sources.
/// An advisory that several places have is kept once, from the file first, then from the downloads in the order of their priority.
/// </summary>
/// <param name="options">Where the advisories are.</param>
/// <param name="time">Clock for when they were loaded.</param>
/// <param name="logger">Receives what could not be read.</param>
internal sealed class AdvisoryStore(VulnerabilitiesOptions options, TimeProvider time, ILogger<AdvisoryStore> logger)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Fetched> _fetched = [];
    private volatile Loaded _file = new([], null);
    private volatile Loaded _merged = new([], null);
    private string _stamp = "";

    /// <summary>The advisories in force and when they were last read or fetched.</summary>
    public (IReadOnlyList<OsvAdvisory> Advisories, DateTimeOffset? LoadedAt) Current => (_merged.Advisories, _merged.At);

    /// <summary>Replaces what one source sent, and keeps what the others sent.</summary>
    /// <param name="source">Names the source, so a later fetch of it replaces this one.</param>
    /// <param name="priority">Which copy of an advisory wins when several sources have it; the lowest number wins, and the file wins over all.</param>
    /// <param name="advisories">What the source sent.</param>
    public void SetFetched(string source, int priority, IReadOnlyList<OsvAdvisory> advisories)
    {
        lock (_gate)
        {
            _fetched[source] = new Fetched(priority, advisories, time.GetUtcNow());
            Merge();
        }
    }

    private void Merge()
    {
        var byId = new Dictionary<string, OsvAdvisory>(StringComparer.Ordinal);
        foreach (var advisory in _file.Advisories)
        {
            byId.TryAdd(advisory.Id, advisory);
        }

        foreach (var fetched in _fetched.Values.OrderBy(f => f.Priority))
        {
            foreach (var advisory in fetched.Advisories)
            {
                byId.TryAdd(advisory.Id, advisory);
            }
        }

        var latest = _fetched.Values.Select(f => (DateTimeOffset?)f.At).Append(_file.At).Max();
        _merged = new Loaded([.. byId.Values], latest);
    }

    /// <summary>Reads the advisories if the file or folder changed since the last time. Never throws; what was loaded stays when the new content cannot be read.</summary>
    public void Refresh()
    {
        if (options.Advisories is not { } path)
        {
            return;
        }

        try
        {
            var files = Files(path);
            var stamp = string.Join('|', files.Select(file => $"{file}:{File.GetLastWriteTimeUtc(file).Ticks}:{new FileInfo(file).Length}"));
            if (stamp == _stamp)
            {
                return;
            }

            var advisories = new List<OsvAdvisory>();
            foreach (var file in files)
            {
                advisories.AddRange(Read(file));
            }

            lock (_gate)
            {
                _file = new Loaded(advisories, time.GetUtcNow());
                Merge();
            }

            _stamp = stamp;
            logger.LogInformation("Loaded {Count} advisories from {Path}", advisories.Count, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError("The advisories in '{Path}' cannot be used: {Reason}. What was loaded stays in force.", path, ex.Message);
        }
    }

    private static List<string> Files(string path) =>
        Directory.Exists(path)
            ? [.. Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [path];

    private static IEnumerable<OsvAdvisory> Read(string file) => Parse(File.ReadAllText(file));

    /// <summary>A text holds one advisory or a list of them.</summary>
    /// <param name="text">The JSON.</param>
    /// <returns>The advisories.</returns>
    /// <exception cref="JsonException">The text is not an advisory or a list of them.</exception>
    internal static IEnumerable<OsvAdvisory> Parse(string text)
    {
        if (text.AsSpan().TrimStart().StartsWith("["))
        {
            return JsonSerializer.Deserialize(text, OsvJsonContext.Default.ListOsvAdvisory) ?? [];
        }

        return JsonSerializer.Deserialize(text, OsvJsonContext.Default.OsvAdvisory) is { } one ? [one] : [];
    }

    private sealed record Loaded(IReadOnlyList<OsvAdvisory> Advisories, DateTimeOffset? At);

    private sealed record Fetched(int Priority, IReadOnlyList<OsvAdvisory> Advisories, DateTimeOffset At);
}
