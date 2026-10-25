namespace Plover.Ui;

/// <summary>The static files of the dashboard page, embedded in this assembly so the controller is a single executable.</summary>
public static class UiAssets
{
    /// <summary>The files, by the path they are served at.</summary>
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["index.html"] = "text/html; charset=utf-8",
        ["app.js"] = "text/javascript; charset=utf-8",
        ["styles.css"] = "text/css; charset=utf-8",
        ["logo.svg"] = "image/svg+xml",
    };

    /// <summary>The paths of all embedded files.</summary>
    public static IReadOnlyCollection<string> Paths => [.. ContentTypes.Keys];

    /// <summary>Gets the media type of an embedded file.</summary>
    /// <param name="path">Path of the file, such as <c>app.js</c>.</param>
    /// <returns>The media type, or <see langword="null"/> when there is no such file.</returns>
    public static string? ContentTypeOf(string path) => ContentTypes.GetValueOrDefault(path);

    /// <summary>Opens an embedded file.</summary>
    /// <param name="path">Path of the file, such as <c>app.js</c>.</param>
    /// <returns>A stream with the content, or <see langword="null"/> when there is no such file. The caller disposes it.</returns>
    public static Stream? Open(string path) =>
        ContentTypes.ContainsKey(path) ? typeof(UiAssets).Assembly.GetManifestResourceStream($"wwwroot/{path}") : null;

    /// <summary>Reads an embedded text file completely.</summary>
    /// <param name="path">Path of the file, such as <c>styles.css</c>.</param>
    /// <returns>The text, or <see langword="null"/> when there is no such file.</returns>
    public static string? ReadText(string path)
    {
        using var stream = Open(path);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
