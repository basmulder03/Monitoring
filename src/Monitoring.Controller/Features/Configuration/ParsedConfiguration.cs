using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monitoring.Controller.Features.Configuration;

/// <summary>A central configuration that was read and checked, ready to be applied to agents.</summary>
internal sealed class ParsedConfiguration
{
    private const int MaxSeconds = 3600;
    private static readonly HashSet<string> KnownSettings = new(StringComparer.Ordinal) { "pullSeconds", "edgeFlushSeconds" };

    private readonly SettingsLayer _defaults;
    private readonly IReadOnlyList<OverrideRule> _byLabels;
    private readonly IReadOnlyList<OverrideRule> _byScope;
    private readonly string _hash;

    private ParsedConfiguration(string hash, SettingsLayer defaults, List<OverrideRule> overrides)
    {
        _hash = hash;
        _defaults = defaults;

        // The more specific a rule, the later it applies and the more it counts: labels first, then a scope; within each, the order of the file.
        _byLabels = [.. overrides.Where(rule => rule.Match.Scope is null)];
        _byScope = [.. overrides.Where(rule => rule.Match.Scope is not null)];
    }

    /// <summary>The built-in defaults, used when there is no file.</summary>
    public static ParsedConfiguration BuiltIn { get; } = new("built-in", new SettingsLayer(), []);

    /// <summary>Reads and checks the text of a configuration file.</summary>
    /// <param name="json">The text of the file.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="ConfigurationRejectedException">The text is not valid JSON of the right shape, or a value is out of range.</exception>
    public static ParsedConfiguration Parse(string json)
    {
        ConfigurationFileModel? model;
        try
        {
            model = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.ConfigurationFileModel);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationRejectedException($"The configuration is not valid: {ex.Message}", ex);
        }

        model ??= new ConfigurationFileModel();
        var defaults = model.Defaults ?? new SettingsLayer();
        var overrides = model.Overrides ?? [];

        Check(defaults, "defaults");
        for (var i = 0; i < overrides.Count; i++)
        {
            var rule = overrides[i];
            if (rule?.Match is null || rule.Set is null)
            {
                throw new ConfigurationRejectedException($"overrides[{i}] needs a 'match' and a 'set'.");
            }

            if (rule.Match.Scope is null && (rule.Match.Labels is null || rule.Match.Labels.Count == 0))
            {
                throw new ConfigurationRejectedException($"overrides[{i}].match must name a scope or labels, or it would apply to everyone; use 'defaults' for that.");
            }

            Check(rule.Set, $"overrides[{i}].set");
        }

        foreach (var name in model.Overridable ?? [])
        {
            if (!KnownSettings.Contains(name))
            {
                throw new ConfigurationRejectedException($"'overridable' names '{name}', which is not a setting. The settings are: {string.Join(", ", KnownSettings.Order())}.");
            }
        }

        return new ParsedConfiguration(Hash(json), defaults, overrides);
    }

    /// <summary>Works out the settings of one agent.</summary>
    /// <param name="scope">The scope the agent reports as.</param>
    /// <param name="labels">The labels of that scope.</param>
    /// <returns>The settings; the version covers the whole file and the settings that came out, so it changes when either does.</returns>
    public AgentSettings For(string scope, IReadOnlyDictionary<string, string> labels)
    {
        int? pull = _defaults.PullSeconds;
        int? edges = _defaults.EdgeFlushSeconds;
        foreach (var rule in _byLabels.Concat(_byScope).Where(rule => Matches(rule.Match, scope, labels)))
        {
            pull = rule.Set.PullSeconds ?? pull;
            edges = rule.Set.EdgeFlushSeconds ?? edges;
        }

        var settings = (Pull: pull ?? 15, Edges: edges ?? 10);
        return new AgentSettings($"{_hash}-{settings.Pull}-{settings.Edges}", settings.Pull, settings.Edges);
    }

    /// <summary>Tells whether this is the same file as another, so a rewrite with the same content is not a change.</summary>
    /// <param name="other">The other configuration.</param>
    /// <returns><see langword="true"/> when the files had the same content.</returns>
    public bool SameAs(ParsedConfiguration other) => _hash == other._hash;

    private static bool Matches(OverrideMatch match, string scope, IReadOnlyDictionary<string, string> labels) =>
        (match.Scope is null || string.Equals(match.Scope, scope, StringComparison.Ordinal))
        && (match.Labels is null || match.Labels.All(label => labels.TryGetValue(label.Key, out var value) && value == label.Value));

    private static void Check(SettingsLayer layer, string where)
    {
        foreach (var (name, value) in new[] { ("pullSeconds", layer.PullSeconds), ("edgeFlushSeconds", layer.EdgeFlushSeconds) })
        {
            if (value is < 1 or > MaxSeconds)
            {
                throw new ConfigurationRejectedException($"{where}.{name} must be from 1 to {MaxSeconds} seconds, but is {value}.");
            }
        }
    }

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..12];
}
