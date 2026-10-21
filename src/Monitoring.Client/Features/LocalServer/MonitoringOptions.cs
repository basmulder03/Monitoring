namespace Monitoring.Client.Features.LocalServer;

/// <summary>Settings of the monitoring client.</summary>
public sealed class MonitoringOptions
{
    /// <summary>Environment variable that names the socket the sidecar connects to.</summary>
    public const string SocketPathVariable = "MONITORING_SOCKET";

    /// <summary>Environment variable that turns monitoring off when set to <c>true</c>.</summary>
    public const string DisabledVariable = "MONITORING_DISABLED";

    /// <summary>Where the application listens for the sidecar. Defaults to the value of <c>MONITORING_SOCKET</c>, else a fixed path on the shared volume.</summary>
    public string SocketPath { get; set; } =
        Environment.GetEnvironmentVariable(SocketPathVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : "/var/run/monitoring/monitoring.sock";

    /// <summary>Whether the client runs. Defaults to <see langword="false"/> when <c>MONITORING_DISABLED</c> is <c>true</c>.</summary>
    public bool Enabled { get; set; } =
        !string.Equals(Environment.GetEnvironmentVariable(DisabledVariable), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Name of the service; defaults to the name of the application.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Version of the application; defaults to the informational version of the entry assembly.</summary>
    public string? Version { get; set; }

    /// <summary>How long the health checks may take together before the application counts as Unhealthy.</summary>
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Instrument names left out of the metrics, each an exact name or a prefix ending in <c>*</c>.
    /// Used to keep noisy or sensitive instruments away from the monitoring system.
    /// </summary>
    public List<string> MetricsDeny { get; set; } = [];

    /// <summary>The most series kept; more would grow the memory of the application, so further series are ignored.</summary>
    public int MaxMetricSeries { get; set; } = 1000;
}
