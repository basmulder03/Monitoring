namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>Describes a list answer.</summary>
/// <param name="ResourceVersion">Version of the whole list; a watch started from it sees every later change.</param>
internal sealed record ListMetadata(string? ResourceVersion = null);
