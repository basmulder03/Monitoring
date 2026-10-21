using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.Configuration;

/// <summary>Default <see cref="IAgentConfiguration"/>: holds the latest configuration in memory; nothing is stored.</summary>
/// <param name="logger">Receives each configuration that comes into force.</param>
internal sealed class AgentConfiguration(ILogger<AgentConfiguration> logger) : IAgentConfiguration
{
    /// <summary>What applies before any controller has spoken.</summary>
    public static Config BuiltIn { get; } = new("built-in", [], HeartbeatSeconds: 5, PullSeconds: 15, EdgeFlushSeconds: 10);

    private volatile Config _current = BuiltIn;

    /// <inheritdoc />
    public Config Current => _current;

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
