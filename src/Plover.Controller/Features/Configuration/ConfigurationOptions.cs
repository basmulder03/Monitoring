namespace Plover.Controller.Features.Configuration;

/// <summary>Settings of the configuration slice.</summary>
public sealed class ConfigurationOptions
{
    /// <summary>
    /// The central configuration file (JSON, comments allowed), normally a mounted ConfigMap. Without one, the built-in defaults apply.
    /// The file is input, not state: every replica reads the same file, so they agree without talking to each other.
    /// </summary>
    public string? File { get; set; }

    /// <summary>How often the file is looked at for a change. Polling copes with ConfigMaps, which replace the file instead of changing it.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
