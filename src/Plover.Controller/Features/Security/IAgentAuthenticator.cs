using Plover.Protocol.Messages;

namespace Plover.Controller.Features.Security;

/// <summary>
/// Decides who an agent is from its <see cref="Hello"/>. Authentication is always on; other methods,
/// such as service account tokens or client certificates, plug in by implementing this interface.
/// </summary>
public interface IAgentAuthenticator
{
    /// <summary>Authenticates the agent that sent the hello.</summary>
    /// <param name="hello">The first message of the connection.</param>
    /// <param name="ct">Cancels the check, which may call out to another system.</param>
    /// <returns>The agent's identity, or <see langword="null"/> when it could not be authenticated.</returns>
    ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default);
}
