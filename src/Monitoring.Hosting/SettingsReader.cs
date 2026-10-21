using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Monitoring.Hosting;

/// <summary>
/// Reads settings from configuration without reflection, so it works in a Native AOT executable,
/// and reports problems with the full name of the setting.
/// </summary>
/// <remarks>
/// Settings can come from a JSON file, environment variables (<c>MONITORING_Agent__ScopeName</c>) or the command line.
/// A list can be written as an array or, handy for environment variables, as one comma-separated value.
/// </remarks>
public sealed class SettingsReader
{
    private readonly IConfiguration _configuration;
    private readonly string _path;

    /// <summary>Creates a reader over the root of the configuration.</summary>
    /// <param name="configuration">The configuration to read.</param>
    public SettingsReader(IConfiguration configuration)
        : this(configuration, "")
    {
    }

    private SettingsReader(IConfiguration configuration, string path)
    {
        _configuration = configuration;
        _path = path;
    }

    /// <summary>Gets a reader for a nested group of settings.</summary>
    /// <param name="name">Name of the group, for example <c>Controller</c>.</param>
    /// <returns>A reader whose keys are relative to the group.</returns>
    public SettingsReader Section(string name) => new(_configuration.GetSection(name), NameOf(name));

    /// <summary>Reads a text setting that must be present.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ConfigurationException">The setting is missing or blank.</exception>
    public string Required(string key) =>
        Optional(key) ?? throw new ConfigurationException($"Setting '{NameOf(key)}' is required.");

    /// <summary>Reads a text setting that may be absent.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing or blank.</returns>
    public string? Optional(string key) =>
        _configuration[key] is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    /// <summary>Reads a text setting with a default.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <param name="fallback">Used when the setting is missing or blank.</param>
    /// <returns>The value or the default.</returns>
    public string Text(string key, string fallback) => Optional(key) ?? fallback;

    /// <summary>Reads a whole number.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <param name="fallback">Used when the setting is missing or blank.</param>
    /// <returns>The value or the default.</returns>
    /// <exception cref="ConfigurationException">The value is not a whole number.</exception>
    public int Int(string key, int fallback)
    {
        var text = Optional(key);
        if (text is null)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ConfigurationException($"Setting '{NameOf(key)}' must be a whole number, but is '{text}'.");
    }

    /// <summary>Reads a duration written as <c>hh:mm:ss</c>, for example <c>00:00:30</c>.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <param name="fallback">Used when the setting is missing or blank.</param>
    /// <returns>The value or the default.</returns>
    /// <exception cref="ConfigurationException">The value is not a duration.</exception>
    public TimeSpan Duration(string key, TimeSpan fallback)
    {
        var text = Optional(key);
        if (text is null)
        {
            return fallback;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ConfigurationException($"Setting '{NameOf(key)}' must be a duration like 00:00:30, but is '{text}'.");
    }

    /// <summary>Reads a list of text values, written as an array or as one comma-separated value.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>The values in order; empty when the setting is missing.</returns>
    public IReadOnlyList<string> List(string key)
    {
        var section = _configuration.GetSection(key);
        var children = section.GetChildren().Where(c => !string.IsNullOrWhiteSpace(c.Value)).Select(c => c.Value!.Trim()).ToList();
        if (children.Count > 0)
        {
            return children;
        }

        return Optional(key) is { } single
            ? [.. single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
    }

    /// <summary>Reads a group of settings as a dictionary, for example labels.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>The name and value of each entry; empty when the setting is missing.</returns>
    public IReadOnlyDictionary<string, string> Map(string key) =>
        _configuration.GetSection(key).GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!.Trim());

    /// <summary>Reads a list of groups, for example the accepted tokens, each with their own settings.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>One reader per entry, in order, named like <c>Controller:AgentTokens:0</c> in messages.</returns>
    public IReadOnlyList<SettingsReader> Items(string key) =>
        [.. _configuration.GetSection(key).GetChildren().Select(c => new SettingsReader(c, NameOf($"{key}:{c.Key}")))];

    /// <summary>Gets the full name of a setting, as it is written in a file, for messages that name it.</summary>
    /// <param name="key">The setting, relative to this reader.</param>
    /// <returns>The path from the root, such as <c>Agent:Transport:Mode</c>.</returns>
    public string NameOf(string key) => _path.Length == 0 ? key : $"{_path}:{key}";
}
