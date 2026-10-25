namespace Plover.Common.Transport;

/// <summary>Protects the stream of a connection; the one place that decides how bytes travel between an agent and a controller.</summary>
public interface ITransportSecurity
{
    /// <summary>Whether what travels over the connection is encrypted.</summary>
    bool IsEncrypted { get; }

    /// <summary>Protects a connection that was accepted, as the server.</summary>
    /// <param name="stream">The accepted connection.</param>
    /// <param name="ct">Cancels the handshake.</param>
    /// <returns>The stream to use from now on; disposing it closes the connection.</returns>
    /// <exception cref="System.Security.Authentication.AuthenticationException">The handshake failed, for example because the peer's certificate is not accepted.</exception>
    /// <exception cref="IOException">The connection broke during the handshake.</exception>
    /// <exception cref="TimeoutException">The handshake took too long.</exception>
    Task<Stream> SecureServerAsync(Stream stream, CancellationToken ct);

    /// <summary>Protects a connection that was opened, as the client.</summary>
    /// <param name="stream">The opened connection.</param>
    /// <param name="host">The host name or address that was connected to, which the server's certificate must match unless a server name is configured.</param>
    /// <param name="ct">Cancels the handshake.</param>
    /// <returns>The stream to use from now on; disposing it closes the connection.</returns>
    /// <exception cref="System.Security.Authentication.AuthenticationException">The handshake failed, for example because the server's certificate is not accepted.</exception>
    /// <exception cref="IOException">The connection broke during the handshake.</exception>
    /// <exception cref="TimeoutException">The handshake took too long.</exception>
    Task<Stream> SecureClientAsync(Stream stream, string host, CancellationToken ct);
}
