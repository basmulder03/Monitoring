using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>The part of the Kubernetes API that leader election needs: read, create and replace one Lease.</summary>
internal interface ILeaseApi
{
    /// <summary>Reads a lease.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="name">The lease name.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The lease, or <see langword="null"/> when there is none yet.</returns>
    /// <exception cref="KubernetesApiException">The API server answered with an error, for example because the role may not read leases.</exception>
    Task<Lease?> GetLeaseAsync(string namespaceName, string name, CancellationToken ct);

    /// <summary>Creates a lease.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="lease">The lease to create.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The lease as stored.</returns>
    /// <exception cref="KubernetesApiException">It exists already (status 409) or the API server answered with another error.</exception>
    Task<Lease> CreateLeaseAsync(string namespaceName, Lease lease, CancellationToken ct);

    /// <summary>Replaces a lease; it fails when the version is no longer the stored one, which is how two agents never both win.</summary>
    /// <param name="namespaceName">The namespace.</param>
    /// <param name="lease">The new contents, with the version they were based on.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The lease as stored.</returns>
    /// <exception cref="KubernetesApiException">Somebody else changed it first (status 409) or the API server answered with another error.</exception>
    Task<Lease> ReplaceLeaseAsync(string namespaceName, Lease lease, CancellationToken ct);
}
