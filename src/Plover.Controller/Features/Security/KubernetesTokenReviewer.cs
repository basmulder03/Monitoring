using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Plover.Controller.Features.Security;

/// <summary>Asks the API server of Kubernetes, which created the token, with a <c>TokenReview</c>. The controller needs the right to create token reviews, which the cluster role <c>system:auth-delegator</c> gives.</summary>
/// <param name="http">Reaches the API server and carries the controller's own credentials.</param>
internal sealed class KubernetesTokenReviewer(HttpClient http) : ITokenReviewer
{
    private const string Path = "apis/authentication.k8s.io/v1/tokenreviews";

    /// <inheritdoc />
    public async ValueTask<string?> ReviewAsync(string token, string audience, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var review = new TokenReview(Spec: new TokenReviewSpec(token, [audience]));
        using var response = await http.PostAsJsonAsync(Path, review, SecurityJsonContext.Default.TokenReview, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var status = (await response.Content.ReadFromJsonAsync(SecurityJsonContext.Default.TokenReview, timeout.Token).ConfigureAwait(false))?.Status;

        // The audience is checked here too, whatever the server did with the one asked for: a token made for another service must never open this one.
        return status is { Authenticated: true, User.Username: { Length: > 0 } user } && status.Audiences?.Contains(audience, StringComparer.Ordinal) == true ? user : null;
    }

    /// <summary>Creates the client for the API server of the cluster the controller runs in, or the one it was told about.</summary>
    /// <param name="options">The address, the credentials and the authority, each optional.</param>
    /// <param name="environment">Reads an environment variable; the pod of a cluster is told where the API server is through them.</param>
    /// <returns>The client, whose requests carry the token of the controller, read again for each since Kubernetes rotates it.</returns>
    /// <exception cref="ArgumentException">There is no address and the controller does not run in a cluster.</exception>
    public static HttpClient CreateClient(ServiceAccountOptions options, Func<string, string?> environment)
    {
        const string InCluster = "/var/run/secrets/kubernetes.io/serviceaccount";
        var address = options.ApiServer
            ?? (environment("KUBERNETES_SERVICE_HOST") is { Length: > 0 } host ? $"https://{(host.Contains(':') ? $"[{host}]" : host)}:{environment("KUBERNETES_SERVICE_PORT") ?? "443"}" : null)
            ?? throw new ArgumentException("Accepting service account tokens needs the address of the API server (ServiceAccounts:ApiServer), or a controller that runs in a cluster.", nameof(options));
        var tokenFile = options.TokenFile ?? $"{InCluster}/token";
        var caFile = options.CaFile ?? (File.Exists($"{InCluster}/ca.crt") ? $"{InCluster}/ca.crt" : null);

        // The API server is reached directly, never through a proxy of the environment, which is meant for traffic that leaves the cluster.
        var inner = new SocketsHttpHandler { UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (caFile is not null)
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(caFile);
            inner.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) => Trusts(authorities, certificate, errors);
        }

        return new HttpClient(new BearerFileHandler(tokenFile) { InnerHandler = inner }, disposeHandler: true) { BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/") };
    }

    private static bool Trusts(X509Certificate2Collection authorities, X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // Only a chain that the system does not know is acceptable; a wrong server name or a missing certificate never is.
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(new X509Certificate2(certificate));
    }

    /// <summary>Adds the token of the controller to each request, read from its file each time.</summary>
    private sealed class BearerFileHandler(string file) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (File.Exists(file))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", File.ReadAllText(file).Trim());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
