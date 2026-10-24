using System.Net;
using Microsoft.Extensions.Logging;
using Monitoring.Common;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>Default <see cref="ISidecarListener"/>: a TCP accept loop that hands each connection to the handler.</summary>
internal sealed class SidecarListener : ISidecarListener
{
    private readonly TcpConnectionListener _listener;

    /// <summary>Creates the listener; nothing is bound until <see cref="Start"/>.</summary>
    /// <param name="handler">Serves each accepted connection.</param>
    /// <param name="options">Address and port to listen on.</param>
    /// <param name="logger">Receives listener and connection faults.</param>
    public SidecarListener(SidecarConnectionHandler handler, SidecarConnectionsOptions options, ILogger<SidecarListener> logger)
    {
        _listener = new TcpConnectionListener(IPAddress.Parse(options.ListenAddress), options.Port, (stream, peer, ct) => handler.HandlePeerAsync(stream, peer, ct), "sidecars", logger);
    }

    /// <inheritdoc />
    public IPEndPoint? LocalEndPoint => _listener.LocalEndPoint;

    /// <inheritdoc />
    public IPEndPoint Start() => _listener.Start();

    /// <inheritdoc />
    public Task RunAsync(CancellationToken ct) => _listener.RunAsync(ct);
}
