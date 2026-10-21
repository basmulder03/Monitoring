namespace Monitoring.Agent.Features.Discovery;

/// <summary>Settings for discovering the pods of a Kubernetes namespace.</summary>
/// <remarks>
/// Inside a cluster nothing needs to be set: the namespace, the API server address, the token and the certificate authority
/// are read from the pod's service account. The service account needs the <c>get</c>, <c>list</c> and <c>watch</c> verbs on
/// <c>pods</c> in its own namespace, and nothing else.
/// </remarks>
public sealed class KubernetesDiscoveryOptions
{
    /// <summary>The namespace to watch; defaults to the namespace the agent runs in.</summary>
    public string? Namespace { get; set; }

    /// <summary>The API server address, such as <c>https://10.0.0.1:443</c>; defaults to the in-cluster address.</summary>
    public string? ApiServer { get; set; }

    /// <summary>File with the service account token; defaults to the in-cluster token file. Read again for every request.</summary>
    public string? TokenFile { get; set; }

    /// <summary>A fixed token to use instead of a token file; meant for development, for example with a proxy.</summary>
    public string? Token { get; set; }

    /// <summary>PEM file with the certificate authority of the API server; defaults to the in-cluster certificate authority.</summary>
    public string? CaFile { get; set; }

    /// <summary>
    /// How long a watch stays open before the pods are listed again in full, which repairs any event that was missed.
    /// </summary>
    public TimeSpan ResyncInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Shortest wait before retrying after a failure.</summary>
    public TimeSpan RetryMinDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait before retrying; repeated failures grow the wait up to this.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);
}
