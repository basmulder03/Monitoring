using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Hosting;
using Monitoring.Sidecar.Features.AgentLink;
using Monitoring.Sidecar.Features.LibraryLink;
using Monitoring.Sidecar.Features.Probing;

namespace Monitoring.Sidecar.Hosting;

/// <summary>
/// Composes the sidecar: reads its settings and registers every slice. This is the only place that knows all slices.
/// </summary>
/// <remarks>Settings live under <c>Sidecar</c>; docs/configuration.md lists them.</remarks>
internal static class SidecarComposition
{
    /// <summary>Registers every sidecar slice using the settings.</summary>
    /// <param name="builder">The host builder to add the slices to.</param>
    /// <param name="settings">The root of the configuration.</param>
    /// <exception cref="ConfigurationException">A setting is missing or cannot be read.</exception>
    /// <exception cref="ArgumentException">A setting has a value that a slice rejects.</exception>
    public static void Compose(IHostApplicationBuilder builder, SettingsReader settings)
    {
        var sidecar = settings.Section("Sidecar");
        var agent = sidecar.Required("Agent");

        builder.Services
            .AddLibraryLink(options =>
            {
                var library = sidecar.Section("Library");
                options.SocketPath = library.Text("Socket", options.SocketPath);
                options.CallTimeout = library.Duration("CallTimeout", options.CallTimeout);
            })
            .AddProbing(options => options.Interval = sidecar.Section("Probe").Duration("Interval", options.Interval))
            .AddAgentLink(options =>
            {
                options.AgentAddress = agent;
                options.InstanceId = sidecar.Text("InstanceId", options.InstanceId);
                options.Labels = new Dictionary<string, string>(sidecar.Map("Labels"));

                var reconnect = sidecar.Section("Reconnect");
                options.ReconnectMinDelay = reconnect.Duration("MinDelay", options.ReconnectMinDelay);
                options.ReconnectMaxDelay = reconnect.Duration("MaxDelay", options.ReconnectMaxDelay);
                options.ConfigReplyTimeout = sidecar.Duration("ConfigReplyTimeout", options.ConfigReplyTimeout);
            });
    }

    /// <summary>Builds a sidecar host from settings; used by tests to start a whole sidecar in process.</summary>
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
