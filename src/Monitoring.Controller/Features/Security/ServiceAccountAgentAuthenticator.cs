using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Security;

/// <summary>
/// Authenticates agents by the token of their Kubernetes service account, which the API server checks. The agent may only report as the scope of the namespace
/// of its service account, so an agent in one namespace can never report for another, and nobody needs a secret that has to be handed out and rotated.
/// </summary>
/// <param name="reviewer">Asks the API server who a token belongs to.</param>
/// <param name="options">The audience and which namespace reports as which scope.</param>
/// <param name="logger">Receives the reason when the API server cannot be asked; optional.</param>
internal sealed class ServiceAccountAgentAuthenticator(ITokenReviewer reviewer, ServiceAccountOptions options, ILogger? logger = null) : IAgentAuthenticator
{
    private const string UserPrefix = "system:serviceaccount:";

    /// <inheritdoc />
    public async ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default)
    {
        // A token of this kind is a JWT, three parts with dots between. Anything else is not worth a question to the API server.
        if (string.IsNullOrEmpty(hello.Token) || hello.Token.Count(c => c == '.') != 2)
        {
            return null;
        }

        string? user;
        try
        {
            user = await reviewer.ReviewAsync(hello.Token, options.Audience, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // The fault is the controller's, not the agent's, but an agent that cannot be checked cannot be let in; it tries again by itself.
            logger?.LogWarning("Cannot check a service account token with the API server: {Reason}", ex.Message);
            return null;
        }

        if (user is null || !user.StartsWith(UserPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = user[UserPrefix.Length..].Split(':');
        if (parts is not [{ Length: > 0 } serviceAccountNamespace, { Length: > 0 }])
        {
            return null;
        }

        var scope = options.Scopes.GetValueOrDefault(serviceAccountNamespace, serviceAccountNamespace);
        var announced = hello.ScopeName?.Trim();

        // An agent that announces nothing reports as its own scope, and never as another.
        return string.IsNullOrEmpty(announced) || announced == scope ? new AgentIdentity(scope) : null;
    }
}
