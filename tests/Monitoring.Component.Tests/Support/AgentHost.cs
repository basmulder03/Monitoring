using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SidecarConnections;
using Monitoring.Agent.Hosting;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Tests.Shared;

namespace Monitoring.Component.Tests.Support;

/// <summary>An agent host built from settings, pointed at a controller.</summary>
internal sealed class AgentHost(string scope, string token) : IAsyncDisposable
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Agent:Reconnect:MinDelay"] = "00:00:00.050",
        ["Agent:Reconnect:MaxDelay"] = "00:00:00.200",
    };

    private IHost? _host;

    /// <summary>The address sidecars connect to; only available when the agent listens for sidecars.</summary>
    public string SidecarAddress => $"127.0.0.1:{_host!.Services.GetRequiredService<ISidecarListener>().LocalEndPoint!.Port}";

    /// <summary>Adds or replaces a setting.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This host, for chaining.</returns>
    public AgentHost With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Makes the agent discover the pods of the (fake) cluster instead of waiting for instances to be reported.</summary>
    /// <param name="cluster">The fake API server.</param>
    /// <returns>This host, for chaining.</returns>
    public AgentHost DiscoveringIn(FakeKubernetesApiServer cluster) =>
        With("Agent:Discovery:Kind", "kubernetes")
            .With("Agent:Discovery:Kubernetes:Namespace", "shop")
            .With("Agent:Discovery:Kubernetes:ApiServer", cluster.Address)
            .With("Agent:Discovery:Kubernetes:Token", "cluster-token")
            .With("Agent:Discovery:Kubernetes:RetryMinDelay", "00:00:00.050")
            .With("Agent:Discovery:Kubernetes:RetryMaxDelay", "00:00:00.200")
            .With("Agent:Sidecars:Listen:Address", "127.0.0.1")
            .With("Agent:Sidecars:Listen:Port", "0");

    public AgentHost ConnectingTo(ControllerHost controller)
    {
        _settings["Agent:Controllers"] = controller.Address;
        return this;
    }

    /// <summary>Makes the agent use TLS, trusting the authority and checking the controller's certificate for the name; with a client certificate it uses mutual TLS.</summary>
    public AgentHost WithTls(TestPki trusted, string controllerName, TestPki.Issued? clientCertificate = null)
    {
        _settings["Agent:Transport:Mode"] = clientCertificate is null ? "tls" : "mtls";
        _settings["Agent:Transport:CaFile"] = trusted.CaFile;
        _settings["Agent:Transport:ServerName"] = controllerName;
        if (clientCertificate is not null)
        {
            _settings["Agent:Transport:Certificate"] = clientCertificate.CertificateFile;
            _settings["Agent:Transport:Key"] = clientCertificate.KeyFile;
        }

        return this;
    }

    public AgentHost Start()
    {
        _settings["Agent:ScopeName"] = scope;
        _settings["Agent:Token"] = token;
        _host = AgentComposition.CreateHost([], _settings, Files.Missing());
        _host.StartAsync().GetAwaiter().GetResult();
        return this;
    }

    /// <summary>Makes the agent report a running instance, as discovery will.</summary>
    public void Report(string instanceId)
    {
        var store = _host!.Services.GetRequiredService<IScopeStateStore>();
        store.Upsert(new InstanceInfo(instanceId, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));
        store.Publish();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
