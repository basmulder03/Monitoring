namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>What a pod is doing now.</summary>
/// <param name="Phase">Kubernetes phase: Pending, Running, Succeeded, Failed or Unknown.</param>
/// <param name="PodIP">IP address of the pod.</param>
/// <param name="Conditions">Conditions such as Ready.</param>
/// <param name="ContainerStatuses">State of each container.</param>
internal sealed record PodStatus(
    string? Phase = null,
    string? PodIP = null,
    List<PodCondition>? Conditions = null,
    List<ContainerStatus>? ContainerStatuses = null);
