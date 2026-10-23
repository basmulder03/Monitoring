namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A Service, the stable name and address that callers use for the pods it selects.</summary>
/// <param name="Metadata">Its name.</param>
/// <param name="Spec">Which pods it selects and its address.</param>
internal sealed record KubernetesService(PodMetadata? Metadata = null, ServiceSpec? Spec = null);

/// <summary>What a Service selects and where it can be reached.</summary>
/// <param name="Selector">The labels a pod must carry; a Service without any selects no pods.</param>
/// <param name="ClusterIP">The virtual address inside the cluster, or <c>None</c> for a headless Service.</param>
internal sealed record ServiceSpec(Dictionary<string, string>? Selector = null, string? ClusterIP = null);

/// <summary>Answer to listing the Services of a namespace.</summary>
/// <param name="Items">The Services.</param>
internal sealed record ServiceList(List<KubernetesService>? Items = null);
