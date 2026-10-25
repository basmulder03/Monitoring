namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>What a pod is doing now.</summary>
/// <param name="Phase">Kubernetes phase: Pending, Running, Succeeded, Failed or Unknown.</param>
/// <param name="PodIP">IP address of the pod.</param>
/// <param name="Conditions">Conditions such as Ready.</param>
/// <param name="ContainerStatuses">State of each container.</param>
/// <param name="InitContainerStatuses">State of each init container.</param>
/// <param name="PodIPs">Every IP address of the pod, which are two for a pod with both IPv4 and IPv6.</param>
internal sealed record PodStatus(
    string? Phase = null,
    string? PodIP = null,
    List<PodCondition>? Conditions = null,
    List<ContainerStatus>? ContainerStatuses = null,
    List<ContainerStatus>? InitContainerStatuses = null,
    List<PodIPAddress>? PodIPs = null);

/// <summary>One IP address of a pod.</summary>
/// <param name="Ip">The address.</param>
internal sealed record PodIPAddress(string? Ip = null);
