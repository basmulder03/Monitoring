namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container that is running.</summary>
/// <param name="StartedAt">When the container started, as Kubernetes reports it.</param>
internal sealed record ContainerStateRunning(string? StartedAt = null);
