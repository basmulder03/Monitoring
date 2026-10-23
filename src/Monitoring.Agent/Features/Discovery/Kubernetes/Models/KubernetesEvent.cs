namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>An event the cluster recorded about an object, such as a pod that cannot be scheduled.</summary>
/// <param name="InvolvedObject">The object the event is about.</param>
/// <param name="Reason">Short machine-friendly cause, for example <c>FailedScheduling</c>.</param>
/// <param name="Message">What happened, in words.</param>
/// <param name="LastTimestamp">When it last happened.</param>
internal sealed record KubernetesEvent(ObjectReference? InvolvedObject = null, string? Reason = null, string? Message = null, DateTimeOffset? LastTimestamp = null);

/// <summary>The object an event is about.</summary>
/// <param name="Kind">The kind of object, for example <c>Pod</c>.</param>
/// <param name="Name">Its name.</param>
internal sealed record ObjectReference(string? Kind = null, string? Name = null);
