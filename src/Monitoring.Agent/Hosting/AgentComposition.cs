using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.Configuration;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.MetricsHistory;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SelfMonitoring;
using Monitoring.Agent.Features.SidecarConnections;
using Monitoring.Common.Prometheus;
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
    /// <summary>The name the agent is installed under as a Windows service.</summary>
    public const string WindowsServiceName = "MonitoringAgent";

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

        // Does nothing unless the agent was started by the Service Control Manager, and then lets it be stopped and logs to the event log.
        builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceName);

        builder.Services
            .AddScopeState(options =>
            {
                options.ScopeName = scopeName;
                options.Labels = new Dictionary<string, string>(agent.Map("Labels"));
            })
            .AddAgentConfiguration()
            .AddSelfMonitoring(options =>
            {
                var self = agent.Section("SelfMonitoring");
                options.Interval = self.Duration("Interval", options.Interval);
                options.PodName = self.Text("PodName", options.PodName);
            })
            .AddMetricsHistory(options =>
            {
                var history = agent.Section("MetricsHistory");
                options.Samples = history.Int("Samples", options.Samples);
                options.MaxSeriesPerInstance = history.Int("MaxSeriesPerInstance", options.MaxSeriesPerInstance);
            })
            .AddPrometheus(
                options => PrometheusSettings.Read(agent.Section("Prometheus"), options),
                provider =>
                {
                    var self = provider.GetRequiredService<SelfReportService>();
                    return () => self.Read().Select(series => new PrometheusSample(series.Name, series.Labels, series.Value));
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
        var kind = agent.Section("Discovery").Text("Kind", "none").ToLowerInvariant();
        var windows = kind == "windows";
        if (sidecars.Text("Enabled", kind is "kubernetes" or "windows" ? "true" : "false").Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSidecarConnections(options => ReadSidecars(sidecars, options, windows));
        }
    }

    private static void ReadSidecars(SettingsReader sidecars, SidecarConnectionsOptions options, bool windows)
    {
        var listen = sidecars.Section("Listen");
        options.ListenAddress = listen.Text("Address", options.ListenAddress);
        options.Port = listen.Int("Port", options.Port);
        options.HelloTimeout = sidecars.Duration("HelloTimeout", options.HelloTimeout);
        options.HeartbeatInterval = sidecars.Duration("HeartbeatInterval", options.HeartbeatInterval);
        options.MissedHeartbeats = sidecars.Int("MissedHeartbeats", options.MissedHeartbeats);
        options.HealthPullInterval = sidecars.Duration("HealthPullInterval", options.HealthPullInterval);
        // On a Windows server the applications connect over a pipe, so no port is opened unless asked for.
        options.TcpEnabled = listen.Text("Enabled", windows ? "false" : "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        options.PipeName = sidecars.Section("Pipe").Optional("Name") ?? (windows ? "monitoring-agent" : null);
        options.PipeAllow = [.. sidecars.Section("Pipe").List("Allow")];
    }

    private static void ReadDiscovery(SettingsReader discovery, DiscoveryOptions options)
    {
        options.Kind = discovery.Text("Kind", "none").ToLowerInvariant() switch
        {
            "none" => DiscoveryKind.None,
            "kubernetes" => DiscoveryKind.Kubernetes,
            "windows" => DiscoveryKind.Windows,
            var other => throw new ConfigurationException($"Setting 'Agent:Discovery:Kind' must be 'none', 'kubernetes' or 'windows', but is '{other}'."),
        };

        var windows = discovery.Section("Windows");
        options.Windows.Services = [.. windows.List("Services")];
        options.Windows.PollInterval = windows.Duration("PollInterval", options.Windows.PollInterval);
        options.Windows.MeasureInterval = windows.Duration("MeasureInterval", options.Windows.MeasureInterval);

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
