namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>The part of the Kubernetes API that discovery needs: list the pods of a namespace and watch them.</summary>
internal interface IKubernetesApi
{
    /// <summary>Lists the pods of a namespace.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The pods and the version to start watching from.</returns>
    /// <exception cref="KubernetesApiException">The API server answered with an error.</exception>
    Task<PodListResult> ListPodsAsync(string namespaceName, CancellationToken ct);

    /// <summary>Streams the changes to the pods of a namespace after a version, until the server or the duration ends it.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="resourceVersion">Version to continue from, as returned by <see cref="ListPodsAsync"/> or an earlier event.</param>
    /// <param name="duration">How long the server may keep the watch open; the sequence then ends normally.</param>
    /// <param name="ct">Ends the stream.</param>
    /// <returns>The events in order.</returns>
    /// <exception cref="KubernetesResourceExpiredException">The version is too old; list again.</exception>
    /// <exception cref="KubernetesApiException">The API server answered with an error or something unreadable.</exception>
    IAsyncEnumerable<PodEvent> WatchPodsAsync(string namespaceName, string resourceVersion, TimeSpan duration, CancellationToken ct);
}
