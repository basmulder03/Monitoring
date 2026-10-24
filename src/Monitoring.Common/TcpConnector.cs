using Monitoring.Common;
using System.Net.Sockets;

namespace Monitoring.Common;

/// <summary>Opens plain TCP connections to an address written as <c>host:port</c>.</summary>
public static class TcpConnector
{
    /// <summary>
    /// How long a connection may take to open. Without it a connect to an address that no longer answers, such as a pod that was just replaced and is still in a list of
    /// endpoints somewhere, waits for the operating system to give up, which takes more than two minutes.
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Connects to a replica.</summary>
    /// <param name="address">The server as <c>host:port</c>.</param>
    /// <param name="ct">Cancels the attempt.</param>
    /// <returns>A stream that closes the connection when disposed.</returns>
    /// <exception cref="ArgumentException">The address is not <c>host:port</c>.</exception>
    /// <exception cref="SocketException">The server cannot be reached, or does not answer within 10 seconds.</exception>
    public static async ValueTask<Stream> ConnectAsync(string address, CancellationToken ct)
    {
        if (!HostPort.TryParse(address, out var host, out var port))
        {
            throw new ArgumentException($"'{address}' is not a host:port address.", nameof(address));
        }

        var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return new NetworkStream(client.Client, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new SocketException((int)SocketError.TimedOut);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
