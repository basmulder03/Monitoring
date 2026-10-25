using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Sidecar.Hosting;

namespace Plover.Component.Tests.Support;

/// <summary>An application with the Plover client, and the sidecar that would run next to it, talking over a real Unix socket.</summary>
internal sealed class ApplicationHost(string instanceId) : IAsyncDisposable
{
    private readonly string _socket = Path.Combine(Path.GetTempPath(), $"mon-{Guid.NewGuid():N}.sock");
    private readonly Dictionary<string, HealthStatus> _checks = [];
    private readonly Dictionary<string, string?> _sidecarSettings = new()
    {
        ["Sidecar:Reconnect:MinDelay"] = "00:00:00.050",
        ["Sidecar:Reconnect:MaxDelay"] = "00:00:00.200",
        ["Sidecar:Probe:Interval"] = "00:00:00.100",
    };

    private readonly List<string> _traceSources = [];
    private IHost? _application;
    private IHost? _sidecar;

    /// <summary>Adds a health check with a fixed result to the application.</summary>
    public ApplicationHost WithCheck(string name, HealthStatus status)
    {
        _checks[name] = status;
        return this;
    }

    /// <summary>Makes the application listen to an activity source, so operations of that source count as its calls.</summary>
    public ApplicationHost WithTraceSource(string name)
    {
        _traceSources.Add(name);
        return this;
    }

    /// <summary>Starts the application, then the sidecar that connects to the agent.</summary>
    public ApplicationHost Start(AgentHost agent)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPlover(o =>
        {
            o.SocketPath = _socket;
            o.ServiceName = "orders";
            o.Version = "1.2.3";
            o.TraceSources = [.. o.TraceSources, .. _traceSources];
        });
        foreach (var (name, status) in _checks)
        {
            builder.Services.AddHealthChecks().AddCheck(name, () => new HealthCheckResult(status, $"{name} is {status}"));
        }

        _application = builder.Build();
        _application.StartAsync().GetAwaiter().GetResult();

        _sidecarSettings["Sidecar:Agent"] = agent.SidecarAddress;
        _sidecarSettings["Sidecar:InstanceId"] = instanceId;
        _sidecarSettings["Sidecar:Library:Socket"] = _socket;
        _sidecar = SidecarComposition.CreateHost([], _sidecarSettings, Files.Missing());
        _sidecar.StartAsync().GetAwaiter().GetResult();
        return this;
    }

    /// <summary>Starts the application reporting over a named pipe to the agent of the machine, as on a Windows server, where there is no sidecar.</summary>
    public ApplicationHost StartOverPipe(string pipeName)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPlover(o =>
        {
            o.SocketPath = _socket;
            o.AgentPipe = pipeName;
            o.ServiceName = "orders";
            o.Version = "1.2.3";
        });
        foreach (var (name, status) in _checks)
        {
            builder.Services.AddHealthChecks().AddCheck(name, () => new HealthCheckResult(status, $"{name} is {status}"));
        }

        _application = builder.Build();
        _application.StartAsync().GetAwaiter().GetResult();
        return this;
    }

    /// <summary>Stops the sidecar the way a pod shutdown does, so it says goodbye.</summary>
    public async Task StopSidecarAsync()
    {
        await _sidecar!.StopAsync();
        _sidecar.Dispose();
        _sidecar = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_sidecar is not null)
        {
            await _sidecar.StopAsync();
            _sidecar.Dispose();
        }

        if (_application is not null)
        {
            await _application.StopAsync();
            _application.Dispose();
        }
    }
}
