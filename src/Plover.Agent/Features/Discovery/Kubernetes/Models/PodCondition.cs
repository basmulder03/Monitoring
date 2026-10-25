namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>One condition of a pod, such as <c>Ready</c>.</summary>
/// <param name="Type">Condition type.</param>
/// <param name="Status">True, False or Unknown.</param>
internal sealed record PodCondition(string Type, string Status);
