using System.Diagnostics;
using System.Net.Sockets;

namespace Plover.Client.Features.Tracing;

/// <summary>
/// Opens TCP connections that show up as calls to a dependency, for protocols with no instrumentation of their own.
/// Raw TCP has no headers, so there is no trace context to pass on: the dependency graph gets an edge to the peer address, and nothing more.
/// </summary>
/// <remarks>Without monitoring the call costs one check that nobody listens, and works like <see cref="TcpClient.ConnectAsync(string, int, CancellationToken)"/>.</remarks>
public static class MonitoredTcp
{
    /// <summary>The activity source of the connections, which the client listens to by default.</summary>
    public const string SourceName = "Plover.Client.Tcp";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>Connects to a peer and counts the attempt, with how long it took and whether it failed, as a call to <c>host:port</c>.</summary>
    /// <param name="host">The name or address of the peer.</param>
    /// <param name="port">The port of the peer.</param>
    /// <param name="ct">Cancels the connection attempt.</param>
    /// <returns>The connected client, which the caller disposes.</returns>
    /// <exception cref="SocketException">The peer cannot be reached.</exception>
    public static async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        using var activity = Source.StartActivity("TCP connect", ActivityKind.Client);
        activity?.SetTag("network.transport", "tcp");
        activity?.SetTag("server.address", host);
        activity?.SetTag("server.port", port);

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return client;
        }
        catch (Exception ex)
        {
            client.Dispose();
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}
