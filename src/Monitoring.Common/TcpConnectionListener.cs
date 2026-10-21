using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Monitoring.Common;

/// <summary>A TCP accept loop: binds an address and hands each accepted connection to a handler until stopped.</summary>
/// <param name="address">The IP address to listen on.</param>
/// <param name="port">The port to listen on; 0 picks a free port.</param>
/// <param name="handle">Serves one connection; the connection is closed when it returns.</param>
/// <param name="what">Names the kind of connection for log messages, such as "agents".</param>
/// <param name="logger">Receives the address that is listened on and connection faults.</param>
public sealed class TcpConnectionListener(
    IPAddress address,
    int port,
    Func<Stream, CancellationToken, Task> handle,
    string what,
    ILogger logger)
{
    private TcpListener? _listener;

    /// <summary>The address and port the listener is bound to, or <see langword="null"/> before <see cref="Start"/>.</summary>
    public IPEndPoint? LocalEndPoint => (IPEndPoint?)_listener?.LocalEndpoint;

    /// <summary>Binds the address, so connections can already be made.</summary>
    /// <returns>The address and port the listener is bound to.</returns>
    /// <exception cref="InvalidOperationException">The listener was already started.</exception>
    /// <exception cref="SocketException">The address or port cannot be bound.</exception>
    public IPEndPoint Start()
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("The listener was already started.");
        }

        var listener = new TcpListener(address, port);
        listener.Start();
        _listener = listener;

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        logger.LogInformation("Listening for {What} on {Endpoint}", what, endpoint);
        return endpoint;
    }

    /// <summary>Accepts and serves connections until cancelled, then closes the listener and waits for the connections to end.</summary>
    /// <param name="ct">Stops the listener.</param>
    /// <returns>A task that completes once every connection has ended.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Start"/> was not called first.</exception>
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
                await handle(client.GetStream(), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected failure while serving a connection from {What}", what);
            }
        }
    }
}
