namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>Answer to listing the events of a namespace.</summary>
/// <param name="Items">The events.</param>
internal sealed record EventList(List<KubernetesEvent>? Items = null);
