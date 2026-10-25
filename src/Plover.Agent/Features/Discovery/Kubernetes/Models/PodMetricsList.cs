namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>Answer of the metrics API for the pods of a namespace.</summary>
/// <param name="Items">The pods.</param>
internal sealed record PodMetricsList(List<PodMetricsItem>? Items = null);

/// <summary>The current use of one pod.</summary>
/// <param name="Metadata">Which pod.</param>
/// <param name="Containers">Use per container.</param>
internal sealed record PodMetricsItem(PodMetadata? Metadata = null, List<ContainerMetrics>? Containers = null);

/// <summary>The current use of one container.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Usage">Quantities by resource (<c>cpu</c>, <c>memory</c>).</param>
internal sealed record ContainerMetrics(string? Name = null, Dictionary<string, string>? Usage = null);
