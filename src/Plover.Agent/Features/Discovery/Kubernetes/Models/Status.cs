namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>An error the API server reports inside a watch stream.</summary>
/// <param name="Code">HTTP-style status code; 410 means the watch version is too old.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="Reason">Machine-readable reason, for example <c>Expired</c>.</param>
internal sealed record Status(int Code = 0, string? Message = null, string? Reason = null);
