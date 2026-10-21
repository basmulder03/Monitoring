namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>What a pod is set up to run.</summary>
/// <param name="NodeName">Node the pod is scheduled on, once it is.</param>
/// <param name="Containers">The containers of the pod.</param>
/// <param name="InitContainers">The containers that run before the application, and native sidecars.</param>
internal sealed record PodSpec(string? NodeName = null, List<ContainerSpec>? Containers = null, List<ContainerSpec>? InitContainers = null);
