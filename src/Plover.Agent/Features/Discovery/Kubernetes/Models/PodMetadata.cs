namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>Identity of a pod.</summary>
/// <param name="Name">Pod name, unique within the namespace.</param>
/// <param name="Uid">Unique id of this incarnation of the pod.</param>
/// <param name="ResourceVersion">Version of the pod object, used to resume a watch.</param>
/// <param name="Labels">Labels set on the pod.</param>
/// <param name="OwnerReferences">The controllers that own the pod, such as a ReplicaSet.</param>
/// <param name="DeletionTimestamp">Set (to the time of deletion) once the pod is terminating.</param>
internal sealed record PodMetadata(
    string Name,
    string? Uid = null,
    string? ResourceVersion = null,
    Dictionary<string, string>? Labels = null,
    List<OwnerReference>? OwnerReferences = null,
    string? DeletionTimestamp = null);
