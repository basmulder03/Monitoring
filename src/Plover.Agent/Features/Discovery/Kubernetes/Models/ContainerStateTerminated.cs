namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container that has exited.</summary>
/// <param name="ExitCode">Exit code of the process.</param>
/// <param name="Reason">Why it ended, for example <c>OOMKilled</c> or <c>Completed</c>.</param>
internal sealed record ContainerStateTerminated(int ExitCode = 0, string? Reason = null);
