namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>Raised when the Kubernetes API answers with an error or something unreadable.</summary>
/// <param name="message">What went wrong.</param>
/// <param name="statusCode">The HTTP-style status code, or 0 when there is none.</param>
internal class KubernetesApiException(string message, int statusCode = 0) : Exception(message)
{
    /// <summary>The HTTP-style status code, or 0 when there is none.</summary>
    public int StatusCode { get; } = statusCode;
}
