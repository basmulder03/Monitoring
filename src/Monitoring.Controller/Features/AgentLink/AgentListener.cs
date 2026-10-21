using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>Default <see cref="IAgentListener"/>: a TCP accept loop that hands each connection to a handler.</summary>
/// <param name="handler">Serves each accepted connection.</param>
/// <param name="options">Address and port to listen on.</param>
/// <param name="logger">Receives listener and connection faults.</param>
internal sealed class AgentListener(
    AgentConnectionHandler handler,
    AgentLinkOptions options,
    ILogger<AgentListener> logger) : IAgentListener
{
    private TcpListener? _listener;

    /// <inheritdoc />
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint;

    /// <inheritdoc />
    public IPEndPoint Start()
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("The agent listener was already started.");
        }

        var listener = new TcpListener(IPAddress.Parse(options.ListenAddress), options.Port);
        listener.Start();
        _listener = listener;

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        logger.LogInformation("Listening for agents on {Endpoint}", endpoint);
        return endpoint;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken ct)
    {
        var listener = _listener ?? throw new InvalidOperationException("Call Start before RunAsync.");
        var connections = new List<Task>();

        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                connections.RemoveAll(task => task.IsCompleted);
                connections.Add(ServeAsync(client, ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown requested.
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    /// <summary>Serves one client; a fault on one connection must never stop the listener.</summary>
    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await handler.HandleAsync(client.GetStream(), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected failure while serving an agent connection");
            }
        }
    }
}
