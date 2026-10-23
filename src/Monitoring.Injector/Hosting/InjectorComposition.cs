using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Hosting;
using Monitoring.Injector.Features.Mutation;

namespace Monitoring.Injector.Hosting;

/// <summary>Composes the injector: reads its settings and starts a small web server that answers the admission webhook over TLS, which the API server demands.</summary>
public static class InjectorComposition
{
    /// <summary>Builds the injector.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="overrides">Settings that win over every other source; used by tests.</param>
    /// <param name="configFile">The configuration file to read instead of the default.</param>
    /// <returns>The host, not yet started.</returns>
    /// <exception cref="ConfigurationException">A setting is missing or cannot be read.</exception>
    public static IHost CreateHost(string[] args, IEnumerable<KeyValuePair<string, string?>>? overrides = null, string? configFile = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args });
        MonitoringHost.Configure(builder, args, overrides, configFile);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        var injector = new SettingsReader(builder.Configuration).Section("Injector");
        var options = new InjectorOptions
        {
            SidecarImage = injector.Required("SidecarImage"),
            ImagePullPolicy = injector.Text("ImagePullPolicy", "IfNotPresent"),
            AgentAddress = injector.Text("AgentAddress", "monitoring-agent:9441"),
            NativeSidecar = injector.Text("NativeSidecar", "true").Equals("true", StringComparison.OrdinalIgnoreCase),
            Labels = new Dictionary<string, string>(injector.Map("Labels"), StringComparer.Ordinal),
        };
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<PodMutator>();

        var web = injector.Section("Web");
        var address = IPAddress.TryParse(web.Text("Address", "0.0.0.0"), out var parsed)
            ? parsed
            : throw new ConfigurationException($"Setting '{web.NameOf("Address")}' must be an IP address.");
        var certificate = new ReloadingCertificate(
            injector.Required("Certificate"), injector.Required("Key"), injector.Duration("CertificateCheckInterval", TimeSpan.FromSeconds(30)), TimeProvider.System,
            LoggerFactory.Create(logging => logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })).CreateLogger("Monitoring.Injector.Certificate")).Load();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(address, web.Int("Port", 8443), listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                // Asked for at every new connection, so a renewed certificate is presented from then on.
                listen.UseHttps(https => https.ServerCertificateSelector = (_, _) => certificate.Current());
            });
        });

        var app = builder.Build();
        app.MapAdmission();
        return app;
    }
}
