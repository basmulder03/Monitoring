namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>Works out where the Kubernetes API is and which namespace to watch, using the in-cluster conventions when nothing is set.</summary>
internal static class KubernetesEnvironment
{
    /// <summary>Directory where Kubernetes mounts the service account into every pod.</summary>
    internal const string ServiceAccountDirectory = "/var/run/secrets/kubernetes.io/serviceaccount";

    /// <summary>Resolves the target and the connection from the options.</summary>
    /// <param name="options">What was configured; anything left out is looked up in the pod's environment.</param>
    /// <param name="environment">Reads an environment variable, returning <see langword="null"/> when it is not set.</param>
    /// <param name="readFile">Reads a text file, returning <see langword="null"/> when it does not exist.</param>
    /// <returns>The namespace to watch and how to reach the API server.</returns>
    /// <exception cref="ArgumentException">The namespace or the API server cannot be determined.</exception>
    public static (DiscoveryTarget Target, KubernetesConnection Connection) Resolve(
        KubernetesDiscoveryOptions options, Func<string, string?> environment, Func<string, string?> readFile)
    {
        var namespaceName = Clean(options.Namespace) ?? Clean(readFile($"{ServiceAccountDirectory}/namespace"))
            ?? throw new ArgumentException(
                "The Kubernetes namespace to watch is not set, and the agent does not run in a pod that provides it.", nameof(options));

        var apiServer = Clean(options.ApiServer) ?? InClusterAddress(environment)
            ?? throw new ArgumentException(
                "The Kubernetes API server address is not set, and the agent does not run in a pod that provides it.", nameof(options));
        if (!Uri.TryCreate(apiServer, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"'{apiServer}' is not an http or https address.", nameof(options));
        }

        // A fixed token replaces the token file, as does an explicit API server without a token file of its own.
        var token = Clean(options.Token);
        var tokenFile = token is not null ? null : Clean(options.TokenFile) ?? ExistingDefault("token", readFile);
        var caFile = address.Scheme == "https" ? Clean(options.CaFile) ?? ExistingDefault("ca.crt", readFile) : null;

        return (new DiscoveryTarget(namespaceName), new KubernetesConnection(address, tokenFile, token, caFile));
    }

    private static string? InClusterAddress(Func<string, string?> environment)
    {
        var host = Clean(environment("KUBERNETES_SERVICE_HOST"));
        var port = Clean(environment("KUBERNETES_SERVICE_PORT")) ?? "443";
        if (host is null)
        {
            return null;
        }

        return host.Contains(':') ? $"https://[{host}]:{port}" : $"https://{host}:{port}";
    }

    private static string? ExistingDefault(string fileName, Func<string, string?> readFile)
    {
        var path = $"{ServiceAccountDirectory}/{fileName}";
        return readFile(path) is null ? null : path;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
