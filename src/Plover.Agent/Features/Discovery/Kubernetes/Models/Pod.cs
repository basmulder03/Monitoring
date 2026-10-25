namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>The part of a Kubernetes pod the agent reads.</summary>
/// <param name="Metadata">Name, labels and owner of the pod.</param>
/// <param name="Spec">What the pod runs.</param>
/// <param name="Status">What the pod is doing now.</param>
internal sealed record Pod(PodMetadata Metadata, PodSpec? Spec = null, PodStatus? Status = null);
