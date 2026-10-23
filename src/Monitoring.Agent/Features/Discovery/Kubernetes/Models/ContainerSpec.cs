namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container of a pod, as configured.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Image">Image the container runs.</param>
/// <param name="RestartPolicy">Set to <c>Always</c> on an init container that is a native sidecar, which keeps running next to the application.</param>
/// <param name="Resources">The requests and limits of the container.</param>
internal sealed record ContainerSpec(string Name, string? Image = null, string? RestartPolicy = null, ResourceRequirements? Resources = null);

/// <summary>What a container asks for and may use, as Kubernetes quantities such as <c>250m</c> or <c>128Mi</c>.</summary>
/// <param name="Requests">What the scheduler reserves, by resource (<c>cpu</c>, <c>memory</c>).</param>
/// <param name="Limits">What the container may not exceed, by resource.</param>
internal sealed record ResourceRequirements(Dictionary<string, string>? Requests = null, Dictionary<string, string>? Limits = null);
