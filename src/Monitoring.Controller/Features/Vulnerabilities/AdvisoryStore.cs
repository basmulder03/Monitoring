using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Holds the advisories read from the file or folder, and reads them again when something there changed.</summary>
/// <param name="options">Where the advisories are.</param>
/// <param name="time">Clock for when they were loaded.</param>
/// <param name="logger">Receives what could not be read.</param>
internal sealed class AdvisoryStore(VulnerabilitiesOptions options, TimeProvider time, ILogger<AdvisoryStore> logger)
{
    private volatile Loaded _loaded = new([], null);
    private string _stamp = "";

    /// <summary>The advisories in force and when they were read.</summary>
    public (IReadOnlyList<OsvAdvisory> Advisories, DateTimeOffset? LoadedAt) Current => (_loaded.Advisories, _loaded.At);

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

            _loaded = new Loaded(advisories, time.GetUtcNow());
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

    /// <summary>A file holds one advisory or a list of them.</summary>
    private static IEnumerable<OsvAdvisory> Read(string file)
    {
        var text = File.ReadAllText(file);
        if (text.AsSpan().TrimStart().StartsWith("["))
        {
            return JsonSerializer.Deserialize(text, OsvJsonContext.Default.ListOsvAdvisory) ?? [];
        }

        return JsonSerializer.Deserialize(text, OsvJsonContext.Default.OsvAdvisory) is { } one ? [one] : [];
    }

    private sealed record Loaded(IReadOnlyList<OsvAdvisory> Advisories, DateTimeOffset? At);
}
