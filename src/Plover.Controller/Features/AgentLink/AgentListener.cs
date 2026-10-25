using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using Plover.Common;
using Plover.Common.Transport;

namespace Plover.Controller.Features.AgentLink;

/// <summary>Default <see cref="IAgentListener"/>: a TCP accept loop that hands each connection to the handler.</summary>
internal sealed class AgentListener : IAgentListener
{
    private readonly TcpConnectionListener _listener;

    /// <summary>Creates the listener; nothing is bound until <see cref="Start"/>.</summary>
    /// <param name="handler">Serves each accepted connection.</param>
    /// <param name="security">Protects each accepted connection before it is served.</param>
    /// <param name="options">Address and port to listen on.</param>
    /// <param name="logger">Receives listener and connection faults.</param>
    public AgentListener(AgentConnectionHandler handler, ITransportSecurity security, AgentLinkOptions options, ILogger<AgentListener> logger)
    {
        _listener = new TcpConnectionListener(
            IPAddress.Parse(options.ListenAddress),
            options.Port,
            (stream, ct) => ServeSecuredAsync(handler, security, logger, stream, ct),
            "agents",
            logger);
    }

    /// <summary>Completes the handshake, then serves; a connection that fails the handshake is dropped with a warning, not an error.</summary>
    private static async Task ServeSecuredAsync(
        AgentConnectionHandler handler, ITransportSecurity security, ILogger logger, Stream stream, CancellationToken ct)
    {
        Stream secured;
        try
        {
            secured = await security.SecureServerAsync(stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or TimeoutException)
        {
            logger.LogWarning("Closing agent connection: the TLS handshake failed: {Reason}", ex.InnerException?.Message ?? ex.Message);
            return;
        }

        await using (secured.ConfigureAwait(false))
        {
            await handler.HandleAsync(secured, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IPEndPoint? LocalEndPoint => _listener.LocalEndPoint;

    /// <inheritdoc />
    public IPEndPoint Start() => _listener.Start();

    /// <inheritdoc />
    public Task RunAsync(CancellationToken ct) => _listener.RunAsync(ct);
}
