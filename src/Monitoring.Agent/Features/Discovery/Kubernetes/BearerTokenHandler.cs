using System.Net.Http.Headers;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>Adds the service account token to every request. The token is read each time because Kubernetes rotates it.</summary>
/// <param name="readToken">Returns the current token, or <see langword="null"/> when there is none.</param>
internal sealed class BearerTokenHandler(Func<string?> readToken) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (readToken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
