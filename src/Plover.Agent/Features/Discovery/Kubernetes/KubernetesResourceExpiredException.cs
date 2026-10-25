namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>Raised when a watch asks for a version the API server no longer has, so the pods must be listed again.</summary>
/// <param name="message">What the API server said.</param>
internal sealed class KubernetesResourceExpiredException(string message) : KubernetesApiException(message, 410);
