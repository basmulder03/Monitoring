using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>One change reported by a watch.</summary>
/// <param name="Type">What happened.</param>
/// <param name="Pod">The pod it happened to; <see langword="null"/> for a bookmark.</param>
/// <param name="ResourceVersion">Version to resume the watch from after this event.</param>
internal sealed record PodEvent(PodEventType Type, Pod? Pod, string? ResourceVersion);
