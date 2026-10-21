using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>The pods of a namespace at one moment.</summary>
/// <param name="ResourceVersion">Version to start a watch from, which sees every change after this list.</param>
/// <param name="Pods">The pods.</param>
internal sealed record PodListResult(string ResourceVersion, IReadOnlyList<Pod> Pods);
