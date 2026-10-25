namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>The controller that owns a pod.</summary>
/// <param name="Kind">Kind of the owner, for example <c>ReplicaSet</c> or <c>StatefulSet</c>.</param>
/// <param name="Name">Name of the owner.</param>
/// <param name="Controller">Whether this owner is the managing controller of the pod.</param>
internal sealed record OwnerReference(string Kind, string Name, bool? Controller = null);
