using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Security;

/// <summary>Accepts an agent that one of several authenticators accepts, asked in order.</summary>
/// <param name="authenticators">The authenticators, the cheapest first.</param>
internal sealed class CompositeAgentAuthenticator(params IAgentAuthenticator[] authenticators) : IAgentAuthenticator
{
    /// <inheritdoc />
    public async ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default)
    {
        foreach (var authenticator in authenticators)
        {
            if (await authenticator.AuthenticateAsync(hello, ct).ConfigureAwait(false) is { } identity)
            {
                return identity;
            }
        }

        return null;
    }
}
