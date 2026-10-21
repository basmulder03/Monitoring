using Monitoring.Protocol.Health;

namespace Monitoring.Protocol.Instances;

/// <summary>One container of a pod, so a problem can be pinned on the application, the monitoring sidecar or another sidecar.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Role">What the container is there for.</param>
/// <param name="Phase">What the container is doing now.</param>
/// <param name="Health">Healthy when running and ready, Degraded when running but not ready, Unhealthy when stuck or exited with an error, Unknown while starting.</param>
/// <param name="Ready">Whether the container passes its readiness check.</param>
/// <param name="Restarts">How often the container restarted.</param>
/// <param name="Image">The image the container runs; <see langword="null"/> when unknown.</param>
/// <param name="ImageDigest">The exact image that runs, as the platform reports it; <see langword="null"/> before the image was pulled.</param>
/// <param name="Reason">Why the container waits or ended, for example <c>CrashLoopBackOff</c> or <c>OOMKilled</c>; <see langword="null"/> when running.</param>
/// <param name="Message">Details about <paramref name="Reason"/>; <see langword="null"/> when there are none.</param>
/// <param name="ExitCode">Exit code of the process once it has exited; <see langword="null"/> otherwise.</param>
/// <param name="LastExitCode">Exit code of the previous run of a container that restarted; <see langword="null"/> when it never ended.</param>
/// <param name="LastReason">Why the previous run ended, for example <c>OOMKilled</c>; <see langword="null"/> when it never ended.</param>
public sealed record ContainerInfo(
    string Name,
    ContainerRole Role,
    ContainerPhase Phase,
    HealthState Health,
    bool Ready = false,
    int Restarts = 0,
    string? Image = null,
    string? ImageDigest = null,
    string? Reason = null,
    string? Message = null,
    int? ExitCode = null,
    int? LastExitCode = null,
    string? LastReason = null);
