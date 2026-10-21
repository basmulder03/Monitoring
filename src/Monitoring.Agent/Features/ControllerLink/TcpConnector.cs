using Monitoring.Common;
using System.Net.Sockets;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Opens plain TCP connections to controller replicas.</summary>
internal static class TcpConnector
{
    /// <summary>Connects to a replica.</summary>
    /// <param name="address">The replica as <c>host:port</c>.</param>
    /// <param name="ct">Cancels the attempt.</param>
    /// <returns>A stream that closes the connection when disposed.</returns>
    /// <exception cref="ArgumentException">The address is not <c>host:port</c>.</exception>
    /// <exception cref="SocketException">The replica cannot be reached.</exception>
    public static async ValueTask<Stream> ConnectAsync(string address, CancellationToken ct)
    {
        if (!HostPort.TryParse(address, out var host, out var port))
        {
            throw new ArgumentException($"'{address}' is not a host:port address.", nameof(address));
        }

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return new NetworkStream(client.Client, ownsSocket: true);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
