namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A Deployment or a StatefulSet, which says how many pods it wants and how many it has.</summary>
/// <param name="Metadata">Its name, and the generation of what it is asked to be.</param>
/// <param name="Spec">What it is asked for.</param>
/// <param name="Status">What it has.</param>
internal sealed record KubernetesWorkload(WorkloadMetadata? Metadata = null, WorkloadSpec? Spec = null, WorkloadStatus? Status = null);

/// <summary>Identity of a workload.</summary>
/// <param name="Name">The name of the workload.</param>
/// <param name="Generation">Increases with every change of what it is asked to be.</param>
internal sealed record WorkloadMetadata(string? Name = null, long? Generation = null);

/// <summary>What a workload is asked for.</summary>
/// <param name="Replicas">How many pods; one when it does not say.</param>
internal sealed record WorkloadSpec(int? Replicas = null);

/// <summary>What a workload has.</summary>
/// <param name="ObservedGeneration">The generation its controller has acted on so far.</param>
/// <param name="ReadyReplicas">How many pods are ready.</param>
/// <param name="UpdatedReplicas">How many pods are of the latest version, ready or not.</param>
/// <param name="Replicas">How many pods exist, of whatever version.</param>
internal sealed record WorkloadStatus(long? ObservedGeneration = null, int? ReadyReplicas = null, int? UpdatedReplicas = null, int? Replicas = null);

/// <summary>Answer to listing the workloads of a namespace.</summary>
/// <param name="Items">The workloads.</param>
internal sealed record WorkloadList(List<KubernetesWorkload>? Items = null);
