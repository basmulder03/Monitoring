namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container of a pod, as configured.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Image">Image the container runs.</param>
internal sealed record ContainerSpec(string Name, string? Image = null);
