namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>Answer to listing the pods of a namespace.</summary>
/// <param name="Metadata">Describes the list, including the version to continue a watch from.</param>
/// <param name="Items">The pods.</param>
internal sealed record PodList(ListMetadata? Metadata = null, List<Pod>? Items = null);
