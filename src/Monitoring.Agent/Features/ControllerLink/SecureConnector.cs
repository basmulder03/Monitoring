using Monitoring.Common;
using Monitoring.Common.Transport;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Opens a TCP connection to a controller replica and protects it with the transport security.</summary>
/// <param name="security">Protects the connection once it is open.</param>
/// <param name="connect">Opens the plain connection.</param>
internal sealed class SecureConnector(ITransportSecurity security, ConnectAsync connect)
{
    /// <summary>Connects to a replica.</summary>
    /// <param name="address">The replica as <c>host:port</c>.</param>
    /// <param name="ct">Cancels the attempt.</param>
    /// <returns>The protected connection; the caller disposes it.</returns>
    public async ValueTask<Stream> ConnectAsync(string address, CancellationToken ct)
    {
        var stream = await connect(address, ct).ConfigureAwait(false);
        if (!security.IsEncrypted)
        {
            return stream;
        }

        try
        {
            HostPort.TryParse(address, out var host, out _);
            return await security.SecureClientAsync(stream, host, ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
