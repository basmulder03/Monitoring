using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Hosting;

namespace Monitoring.Controller.Hosting;

/// <summary>
/// Composes the controller: reads its settings and registers every slice. This is the only place that knows all slices.
/// </summary>
/// <remarks>
/// Settings live under <c>Controller</c>; docs/configuration.md lists them. Anything not set keeps the slice's own default.
/// </remarks>
internal static class ControllerComposition
{
    /// <summary>Registers every controller slice using the settings.</summary>
    /// <param name="builder">The host builder to add the slices to.</param>
    /// <param name="settings">The root of the configuration.</param>
    /// <exception cref="ConfigurationException">A setting is missing or cannot be read.</exception>
    /// <exception cref="ArgumentException">A setting has a value that a slice rejects.</exception>
    public static void Compose(HostApplicationBuilder builder, SettingsReader settings)
    {
        var controller = settings.Section("Controller");
        var tokens = ReadAgentTokens(controller);

        builder.Services
            .AddRegistry(options =>
            {
                var registry = controller.Section("Registry");
                options.GoneRetention = registry.Duration("GoneRetention", options.GoneRetention);
                options.PurgeInterval = registry.Duration("PurgeInterval", options.PurgeInterval);
            })
            .AddHealth()
            .AddAlerting(options =>
            {
                var alerting = controller.Section("Alerting");
                options.StartupGrace = alerting.Duration("StartupGrace", options.StartupGrace);
                options.InstanceUnhealthyWait = alerting.Duration("InstanceUnhealthyWait", options.InstanceUnhealthyWait);
                options.ServiceDownWait = alerting.Duration("ServiceDownWait", options.ServiceDownWait);
                options.EvaluationInterval = alerting.Duration("EvaluationInterval", options.EvaluationInterval);
            })
            .AddSecurity(options => options.AgentTokens.AddRange(tokens))
            .AddAgentLink(options =>
            {
                var listen = controller.Section("Listen");
                options.ListenAddress = listen.Text("Address", options.ListenAddress);
                options.Port = listen.Int("Port", options.Port);
                options.HelloTimeout = controller.Section("AgentLink").Duration("HelloTimeout", options.HelloTimeout);
                options.ControllerReplicas = [.. controller.List("Replicas")];
            });
    }

    /// <summary>Builds a controller host from settings; used by tests to start a whole controller in process.</summary>
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

    private static List<AgentTokenEntry> ReadAgentTokens(SettingsReader controller)
    {
        var tokens = controller.Items("AgentTokens")
            .Select(item => new AgentTokenEntry(item.Required("Scope"), item.Required("TokenHash")))
            .ToList();

        // A controller that accepts no agent is a mistake in the configuration, not a mode of operation.
        return tokens.Count > 0
            ? tokens
            : throw new ConfigurationException("Setting 'Controller:AgentTokens' needs at least one entry with a Scope and a TokenHash.");
    }
}
