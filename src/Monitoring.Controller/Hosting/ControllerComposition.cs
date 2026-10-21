using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
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
    public static void Compose(IHostApplicationBuilder builder, SettingsReader settings)
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
            .AddDashboard(options =>
            {
                var dashboard = controller.Section("Dashboard");
                options.PushInterval = dashboard.Duration("PushInterval", options.PushInterval);
                options.KeepAliveInterval = dashboard.Duration("KeepAliveInterval", options.KeepAliveInterval);
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

    /// <summary>Builds the controller: the slices, the web server for the dashboard and the dashboard's endpoints.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="overrides">Settings that win over every other source; used by tests.</param>
    /// <param name="configFile">The configuration file to read instead of the default.</param>
    /// <returns>The host, not yet started.</returns>
    /// <exception cref="ConfigurationException">A setting is missing or cannot be read.</exception>
    public static IHost CreateHost(
        string[] args, IEnumerable<KeyValuePair<string, string?>>? overrides = null, string? configFile = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
        MonitoringHost.Configure(builder, args, overrides, configFile);

        var settings = new SettingsReader(builder.Configuration);
        Compose(builder, settings);
        ListenOn(builder, settings.Section("Controller").Section("Web"));

        var app = builder.Build();
        app.MapDashboard();
        return app;
    }

    /// <summary>Makes the web server listen where the settings say; plain HTTP, as for the agents, until transport security exists.</summary>
    private static void ListenOn(WebApplicationBuilder builder, SettingsReader web)
    {
        var addressText = web.Text("Address", "0.0.0.0");
        var port = web.Int("Port", 8080);

        if (!IPAddress.TryParse(addressText, out var address))
        {
            throw new ConfigurationException($"Setting 'Controller:Web:Address' must be an IP address, but is '{addressText}'.");
        }

        if (port is < 0 or > 65535)
        {
            throw new ConfigurationException($"Setting 'Controller:Web:Port' must be between 0 and 65535, but is {port}.");
        }

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(address, port);
        });
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
