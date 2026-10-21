using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Hosting;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

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

    /// <summary>Adds or replaces a setting.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This host, for chaining.</returns>
    public AgentHost With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    public AgentHost ConnectingTo(ControllerHost controller)
    {
        _settings["Agent:Controllers"] = controller.Address;
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
