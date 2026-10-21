namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container of a pod, as configured.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Image">Image the container runs.</param>
/// <param name="RestartPolicy">Set to <c>Always</c> on an init container that is a native sidecar, which keeps running next to the application.</param>
internal sealed record ContainerSpec(string Name, string? Image = null, string? RestartPolicy = null);
