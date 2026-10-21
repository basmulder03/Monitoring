using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Controller.Hosting;

namespace Monitoring.Component.Tests.Support;

/// <summary>A controller host built from settings, listening on a free loopback port.</summary>
internal sealed class ControllerHost : IAsyncDisposable
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Controller:Listen:Address"] = "127.0.0.1",
        ["Controller:Listen:Port"] = "0",
        ["Controller:Web:Address"] = "127.0.0.1",
        ["Controller:Web:Port"] = "0",
    };

    private int _tokens;
    private IHost? _host;

    public IScopeRegistry Registry => _host!.Services.GetRequiredService<IScopeRegistry>();

    public string Address => $"127.0.0.1:{_host!.Services.GetRequiredService<IAgentListener>().LocalEndPoint!.Port}";

    /// <summary>The address of the dashboard's web server, such as <c>http://127.0.0.1:5000</c>.</summary>
    public string WebAddress => _host!.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    /// <summary>Adds or replaces a setting.</summary>
    /// <param name="key">The setting.</param>
    /// <param name="value">Its value.</param>
    /// <returns>This host, for chaining.</returns>
    public ControllerHost With(string key, string value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>The address agents connect to, as a plain TCP endpoint.</summary>
    public System.Net.IPEndPoint AgentEndPoint => _host!.Services.GetRequiredService<IAgentListener>().LocalEndPoint!;

    /// <summary>Accepts any agent holding the token, whatever scope it announces.</summary>
    public ControllerHost AcceptingEnrollment(string token) => AcceptingScope(AgentTokenEntry.AnyScope, token);

    /// <summary>Accepts agents holding the token, but only as the given scope.</summary>
    public ControllerHost AcceptingScope(string scope, string token)
    {
        _settings[$"Controller:AgentTokens:{_tokens}:Scope"] = scope;
        _settings[$"Controller:AgentTokens:{_tokens}:TokenHash"] = AgentTokenHash.Compute(token);
        _tokens++;
        return this;
    }

    public ControllerHost Start()
    {
        _host = ControllerComposition.CreateHost([], _settings, Files.Missing());
        _host.StartAsync().GetAwaiter().GetResult();
        return this;
    }

    public IReadOnlyList<string> InstanceIds(string scope) => [.. Registry.GetInstances(scope).Select(i => i.Id)];

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
