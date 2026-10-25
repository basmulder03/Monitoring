namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>How to reach and authenticate with the Kubernetes API server.</summary>
/// <param name="ApiServer">Address of the API server, for example <c>https://10.0.0.1:443</c>.</param>
/// <param name="TokenFile">File holding the service account token; read again for every request because it rotates.</param>
/// <param name="Token">A fixed token, used when there is no <paramref name="TokenFile"/>; meant for development.</param>
/// <param name="CaFile">PEM file with the certificate authority that signed the API server's certificate; the system trust store is used when absent.</param>
internal sealed record KubernetesConnection(Uri ApiServer, string? TokenFile = null, string? Token = null, string? CaFile = null);
