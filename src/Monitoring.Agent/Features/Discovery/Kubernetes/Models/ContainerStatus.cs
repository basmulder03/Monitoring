namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>State of one container of a pod.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Ready">Whether the container passes its readiness check.</param>
/// <param name="RestartCount">How often the container restarted.</param>
/// <param name="State">What the container is doing now.</param>
internal sealed record ContainerStatus(string Name, bool Ready = false, int RestartCount = 0, ContainerState? State = null);
