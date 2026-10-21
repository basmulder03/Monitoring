namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>What a container is doing; exactly one of the parts is set.</summary>
/// <param name="Running">Set while the container runs.</param>
/// <param name="Waiting">Set while the container has not started or keeps failing to.</param>
/// <param name="Terminated">Set once the container has exited.</param>
internal sealed record ContainerState(ContainerStateWaiting? Waiting = null, ContainerStateTerminated? Terminated = null, ContainerStateRunning? Running = null);
