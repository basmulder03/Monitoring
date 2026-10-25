using Microsoft.Extensions.Logging;
using Plover.Protocol.Messages;

namespace Plover.Agent.Features.Configuration;

/// <summary>Default <see cref="IAgentConfiguration"/>: holds the latest configuration in memory; nothing is stored.</summary>
/// <param name="logger">Receives each configuration that comes into force.</param>
/// <param name="local">The local overrides, which count only where the central configuration allows them.</param>
internal sealed class AgentConfiguration(ILogger<AgentConfiguration> logger, AgentConfigurationOptions? local = null) : IAgentConfiguration
{
    /// <summary>What applies before any controller has spoken.</summary>
    public static Config BuiltIn { get; } = new("built-in", [], HeartbeatSeconds: 5, PullSeconds: 15, EdgeFlushSeconds: 10);

    private volatile Config _current = BuiltIn;

    /// <inheritdoc />
    public Config Current => _current;

    /// <inheritdoc />
    public EffectiveSettings SettingsFor(string serviceName)
    {
        var config = _current;
        var settings = config.Services is { } services && services.TryGetValue(serviceName, out var forService)
            ? new EffectiveSettings(forService.PullSeconds, forService.EdgeFlushSeconds)
            : new EffectiveSettings(config.PullSeconds, config.EdgeFlushSeconds);

        // The most specific layer: what is set on this very machine, if the central configuration says it may be.
        var allowed = config.Overridable ?? [];
        return new EffectiveSettings(
            allowed.Contains("pullSeconds") && local?.PullSeconds is { } pull ? pull : settings.PullSeconds,
            allowed.Contains("edgeFlushSeconds") && local?.EdgeFlushSeconds is { } edges ? edges : settings.EdgeFlushSeconds);
    }

    /// <inheritdoc />
    public void Apply(Config config)
    {
        var before = _current;
        if (before.Version == config.Version
            && before.PullSeconds == config.PullSeconds
            && before.EdgeFlushSeconds == config.EdgeFlushSeconds)
        {
            return;
        }

        _current = config;
        logger.LogInformation(
            "Using configuration {Version}: metrics every {Pull} s, dependency calls every {Edges} s",
            config.Version,
            config.PullSeconds,
            config.EdgeFlushSeconds);
    }
}
