namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>State of one container of a pod.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Ready">Whether the container passes its readiness check.</param>
/// <param name="RestartCount">How often the container restarted.</param>
/// <param name="State">What the container is doing now.</param>
/// <param name="LastState">What the container did before it restarted, such as the reason and exit code of the run that ended.</param>
/// <param name="Image">The image the container runs.</param>
/// <param name="ImageID">The exact image that runs, with its digest.</param>
internal sealed record ContainerStatus(
    string Name,
    bool Ready = false,
    int RestartCount = 0,
    ContainerState? State = null,
    ContainerState? LastState = null,
    string? Image = null,
    string? ImageID = null);
