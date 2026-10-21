using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>Creates the HTTP client used to talk to the Kubernetes API server.</summary>
internal static class KubernetesHttpClient
{
    /// <summary>Creates a client for the API server.</summary>
    /// <param name="connection">Where the server is and how to authenticate.</param>
    /// <returns>A client with the base address, credentials and trust set up. It has no overall timeout, because a watch stays open.</returns>
    /// <exception cref="InvalidOperationException">The CA file holds no certificate.</exception>
    public static HttpClient Create(KubernetesConnection connection)
    {
        // The API server is reached directly. A proxy configured through the environment is meant for traffic that
        // leaves the cluster, and sending the agent's calls through it makes them fail.
        var inner = new SocketsHttpHandler { UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (connection.CaFile is { } caFile)
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile(caFile);
            inner.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) => TrustsCertificate(authorities, certificate, errors);
        }

        var handler = new BearerTokenHandler(() => ReadToken(connection)) { InnerHandler = inner };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = connection.ApiServer,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>Reads the current token.</summary>
    /// <param name="connection">The connection settings.</param>
    /// <returns>The token, or <see langword="null"/> when there is none or the file cannot be read right now.</returns>
    internal static string? ReadToken(KubernetesConnection connection)
    {
        if (connection.TokenFile is null)
        {
            return connection.Token;
        }

        try
        {
            return File.ReadAllText(connection.TokenFile).Trim();
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Accepts a server certificate that chains to the configured authority and is valid for the server name.</summary>
    private static bool TrustsCertificate(X509Certificate2Collection authorities, X509Certificate? certificate, SslPolicyErrors errors)
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
}
