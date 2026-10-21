namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A container that is not running yet.</summary>
/// <param name="Reason">Why it is waiting, for example <c>CrashLoopBackOff</c> or <c>ContainerCreating</c>.</param>
/// <param name="Message">Details about the reason.</param>
internal sealed record ContainerStateWaiting(string? Reason = null, string? Message = null);
