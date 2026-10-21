using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SidecarConnections;
using Monitoring.Hosting;

namespace Monitoring.Agent.Hosting;

/// <summary>
/// Composes the agent: reads its settings and registers every slice. This is the only place that knows all slices.
/// </summary>
/// <remarks>
/// Settings live under <c>Agent</c>; docs/configuration.md lists them. These are the bootstrap settings that cannot come from
/// the controller, because the agent needs them to reach it.
/// </remarks>
internal static class AgentComposition
{
    /// <summary>Registers every agent slice using the settings.</summary>
    /// <param name="builder">The host builder to add the slices to.</param>
    /// <param name="settings">The root of the configuration.</param>
    /// <exception cref="ConfigurationException">A setting is missing or cannot be read.</exception>
    /// <exception cref="ArgumentException">A setting has a value that a slice rejects.</exception>
    public static void Compose(HostApplicationBuilder builder, SettingsReader settings)
    {
        var agent = settings.Section("Agent");
        var scopeName = agent.Required("ScopeName");
        var token = agent.Required("Token");
        var controllers = agent.List("Controllers");
        if (controllers.Count == 0)
        {
            throw new ConfigurationException("Setting 'Agent:Controllers' needs at least one controller address as host:port.");
        }

        builder.Services
            .AddScopeState(options =>
            {
                options.ScopeName = scopeName;
                options.Labels = new Dictionary<string, string>(agent.Map("Labels"));
            })
            .AddControllerLink(options =>
            {
                options.ControllerAddresses = [.. controllers];
                options.Token = token;
                options.AgentId = agent.Text("Id", options.AgentId);
                options.AgentVersion = agent.Optional("Version");
                options.Labels = new Dictionary<string, string>(agent.Map("Labels"));

                var reconnect = agent.Section("Reconnect");
                options.ReconnectMinDelay = reconnect.Duration("MinDelay", options.ReconnectMinDelay);
                options.ReconnectMaxDelay = reconnect.Duration("MaxDelay", options.ReconnectMaxDelay);
                options.ConfigReplyTimeout = agent.Duration("ConfigReplyTimeout", options.ConfigReplyTimeout);
                options.SnapshotInterval = agent.Duration("SnapshotInterval", options.SnapshotInterval);
                options.Transport = TransportSettings.Read(agent.Section("Transport"));
            })
            .AddDiscovery(options => ReadDiscovery(agent.Section("Discovery"), options));

        // Sidecars only exist next to pods, so by default the agent listens for them only when it discovers Kubernetes pods.
        var sidecars = agent.Section("Sidecars");
        var kubernetes = agent.Section("Discovery").Text("Kind", "none").Equals("kubernetes", StringComparison.OrdinalIgnoreCase);
        if (sidecars.Text("Enabled", kubernetes ? "true" : "false").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSidecarConnections(options => ReadSidecars(sidecars, options));
        }
    }

    private static void ReadSidecars(SettingsReader sidecars, SidecarConnectionsOptions options)
    {
        var listen = sidecars.Section("Listen");
        options.ListenAddress = listen.Text("Address", options.ListenAddress);
        options.Port = listen.Int("Port", options.Port);
        options.HelloTimeout = sidecars.Duration("HelloTimeout", options.HelloTimeout);
        options.HeartbeatInterval = sidecars.Duration("HeartbeatInterval", options.HeartbeatInterval);
        options.MissedHeartbeats = sidecars.Int("MissedHeartbeats", options.MissedHeartbeats);
        options.HealthPullInterval = sidecars.Duration("HealthPullInterval", options.HealthPullInterval);
        options.MetricsPullInterval = sidecars.Duration("MetricsPullInterval", options.MetricsPullInterval);
        options.EdgesPullInterval = sidecars.Duration("EdgesPullInterval", options.EdgesPullInterval);
    }

    private static void ReadDiscovery(SettingsReader discovery, DiscoveryOptions options)
    {
        options.Kind = discovery.Text("Kind", "none").ToLowerInvariant() switch
        {
            "none" => DiscoveryKind.None,
            "kubernetes" => DiscoveryKind.Kubernetes,
            var other => throw new ConfigurationException($"Setting 'Agent:Discovery:Kind' must be 'none' or 'kubernetes', but is '{other}'."),
        };

        var kubernetes = discovery.Section("Kubernetes");
        options.Kubernetes.Namespace = kubernetes.Optional("Namespace");
        options.Kubernetes.ApiServer = kubernetes.Optional("ApiServer");
        options.Kubernetes.TokenFile = kubernetes.Optional("TokenFile");
        options.Kubernetes.Token = kubernetes.Optional("Token");
        options.Kubernetes.CaFile = kubernetes.Optional("CaFile");
        options.Kubernetes.MonitoringSidecarContainer = kubernetes.Text("MonitoringSidecarContainer", options.Kubernetes.MonitoringSidecarContainer);
        options.Kubernetes.ResyncInterval = kubernetes.Duration("ResyncInterval", options.Kubernetes.ResyncInterval);
        options.Kubernetes.RetryMinDelay = kubernetes.Duration("RetryMinDelay", options.Kubernetes.RetryMinDelay);
        options.Kubernetes.RetryMaxDelay = kubernetes.Duration("RetryMaxDelay", options.Kubernetes.RetryMaxDelay);
    }

    /// <summary>Builds an agent host from settings; used by tests to start a whole agent in process.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="overrides">Settings that win over every other source.</param>
    /// <param name="configFile">The configuration file to read instead of the default.</param>
    /// <returns>The host, not yet started.</returns>
    public static IHost CreateHost(
        string[] args, IEnumerable<KeyValuePair<string, string?>>? overrides = null, string? configFile = null)
    {
        var builder = MonitoringHost.CreateBuilder(args, overrides, configFile);
        Compose(builder, new SettingsReader(builder.Configuration));
        return builder.Build();
    }
}
