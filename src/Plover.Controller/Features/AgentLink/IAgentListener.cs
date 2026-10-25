using System.Net;

namespace Plover.Controller.Features.AgentLink;

/// <summary>Accepts connections from agents and applies the state they send.</summary>
public interface IAgentListener
{
    /// <summary>The address and port the listener is bound to, or <see langword="null"/> before <see cref="Start"/>.</summary>
    IPEndPoint? LocalEndPoint { get; }

    /// <summary>Starts listening, so agents can already connect.</summary>
    /// <returns>The address and port the listener is bound to; useful when the configured port is 0.</returns>
    /// <exception cref="InvalidOperationException">The listener was already started.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">The address or port cannot be bound.</exception>
    IPEndPoint Start();

    /// <summary>Accepts and serves connections until cancelled, then closes them and the listener.</summary>
    /// <param name="ct">Stops the listener.</param>
    /// <returns>A task that completes once every connection has ended.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Start"/> was not called first.</exception>
    Task RunAsync(CancellationToken ct);
}
