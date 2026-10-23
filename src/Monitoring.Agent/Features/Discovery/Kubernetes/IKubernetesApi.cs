using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

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

    /// <summary>Lists the Deployments and StatefulSets of a namespace, which say how many pods they want and have.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The workloads by key, as <see cref="Rollout.KeyOf"/> makes it for their pods.</returns>
    /// <exception cref="KubernetesApiException">The API server answered with an error, for example because the role may not read them.</exception>
    Task<IReadOnlyDictionary<string, KubernetesWorkload>> ListWorkloadsAsync(string namespaceName, CancellationToken ct);

    /// <summary>Lists the warning events of a namespace, which tell why a pod does not start.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The events, in no particular order.</returns>
    /// <exception cref="KubernetesApiException">The API server answered with an error, for example because the role may not read events.</exception>
    Task<IReadOnlyList<KubernetesEvent>> ListWarningEventsAsync(string namespaceName, CancellationToken ct);

    /// <summary>Reads what the pods of a namespace use right now, from the metrics API (the metrics server).</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The use per pod name.</returns>
    /// <exception cref="KubernetesApiException">The API server answered with an error, for example because there is no metrics server or the role may not read it.</exception>
    Task<IReadOnlyDictionary<string, PodUsage>> ListPodUsageAsync(string namespaceName, CancellationToken ct);
}

/// <summary>What a pod uses right now, added up over its containers.</summary>
/// <param name="CpuCores">Processor use in cores.</param>
/// <param name="MemoryBytes">Memory use in bytes.</param>
internal sealed record PodUsage(double CpuCores, double MemoryBytes);
