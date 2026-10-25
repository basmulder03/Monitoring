namespace Plover.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>A coordination.k8s.io Lease: a named record that one holder at a time keeps renewing.</summary>
/// <param name="Metadata">Name and version of the object; the version makes an update fail when somebody else changed it first.</param>
/// <param name="Spec">Who holds it and since when.</param>
/// <param name="ApiVersion">Always <c>coordination.k8s.io/v1</c>.</param>
/// <param name="Kind">Always <c>Lease</c>.</param>
internal sealed record Lease(
    LeaseMetadata Metadata,
    LeaseSpec Spec,
    string ApiVersion = "coordination.k8s.io/v1",
    string Kind = "Lease");

/// <summary>Identity of a lease.</summary>
/// <param name="Name">Lease name, unique within the namespace.</param>
/// <param name="ResourceVersion">Version of the object, as the API server last gave it.</param>
internal sealed record LeaseMetadata(string Name, string? ResourceVersion = null);

/// <summary>The contents of a lease.</summary>
/// <param name="HolderIdentity">Who holds it; empty once it was given up.</param>
/// <param name="LeaseDurationSeconds">How long a holder keeps it without renewing.</param>
/// <param name="RenewTime">When the holder last renewed it, as the holder wrote it (RFC 3339 with microseconds).</param>
/// <param name="AcquireTime">When the current holder took it.</param>
/// <param name="LeaseTransitions">How many times the holder changed.</param>
internal sealed record LeaseSpec(
    string? HolderIdentity = null,
    int? LeaseDurationSeconds = null,
    string? RenewTime = null,
    string? AcquireTime = null,
    int? LeaseTransitions = null);
