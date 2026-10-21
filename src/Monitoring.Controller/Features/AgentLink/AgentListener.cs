using System.Net;
using Microsoft.Extensions.Logging;
using Monitoring.Common;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>Default <see cref="IAgentListener"/>: a TCP accept loop that hands each connection to the handler.</summary>
internal sealed class AgentListener : IAgentListener
{
    private readonly TcpConnectionListener _listener;

    /// <summary>Creates the listener; nothing is bound until <see cref="Start"/>.</summary>
    /// <param name="handler">Serves each accepted connection.</param>
    /// <param name="options">Address and port to listen on.</param>
    /// <param name="logger">Receives listener and connection faults.</param>
    public AgentListener(AgentConnectionHandler handler, AgentLinkOptions options, ILogger<AgentListener> logger)
    {
        _listener = new TcpConnectionListener(IPAddress.Parse(options.ListenAddress), options.Port, handler.HandleAsync, "agents", logger);
    }

    /// <inheritdoc />
    public IPEndPoint? LocalEndPoint => _listener.LocalEndPoint;

    /// <inheritdoc />
    public IPEndPoint Start() => _listener.Start();

    /// <inheritdoc />
    public Task RunAsync(CancellationToken ct) => _listener.RunAsync(ct);
}
