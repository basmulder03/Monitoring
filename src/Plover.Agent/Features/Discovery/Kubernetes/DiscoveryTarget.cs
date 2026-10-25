namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>What the Kubernetes discovery watches.</summary>
/// <param name="Namespace">The namespace whose pods become instances.</param>
internal sealed record DiscoveryTarget(string Namespace);
