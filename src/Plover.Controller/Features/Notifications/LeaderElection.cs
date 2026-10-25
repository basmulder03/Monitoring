using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Notifications;

/// <summary>Every replica sends; the receiver de-duplicates by the stable key of the alert.</summary>
internal sealed class AlwaysLeader : ILeaderElection
{
    /// <inheritdoc />
    public bool IsLeader => true;
}

/// <summary>
/// One replica sends: the one that ranks first among the replicas it can reach, which needs no shared Kubernetes cluster and so also works with replicas in several data centers.
/// </summary>
/// <param name="peers">What this replica knows of the others.</param>
internal sealed class PeerLeaderElection(Alerting.IReplicaPeers peers) : ILeaderElection
{
    /// <inheritdoc />
    public bool IsLeader => peers.IsFirst;
}

/// <summary>A Lease as the API server of Kubernetes holds it.</summary>
/// <param name="Metadata">Its name and version.</param>
/// <param name="Spec">Who holds it, and until when.</param>
/// <param name="ApiVersion">The API group and version of the kind.</param>
/// <param name="Kind">The kind, always <c>Lease</c>.</param>
internal sealed record KubernetesLease(LeaseMetadata Metadata, LeaseSpec Spec, string ApiVersion = "coordination.k8s.io/v1", string Kind = "Lease");

/// <summary>Identity of a Lease.</summary>
/// <param name="Name">Its name.</param>
/// <param name="ResourceVersion">Its version, which makes an update fail when someone else changed it first.</param>
internal sealed record LeaseMetadata(string Name, string? ResourceVersion = null);

/// <summary>Who holds a Lease.</summary>
/// <param name="HolderIdentity">The holder.</param>
/// <param name="LeaseDurationSeconds">How long the holder may stay silent.</param>
/// <param name="AcquireTime">When the holder took it.</param>
/// <param name="RenewTime">When the holder last said it still holds it.</param>
internal sealed record LeaseSpec(string? HolderIdentity, int LeaseDurationSeconds, string? AcquireTime = null, string? RenewTime = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KubernetesLease))]
internal sealed partial class LeaseJsonContext : JsonSerializerContext;

/// <summary>The part of the Kubernetes API that the election uses.</summary>
internal interface ILeaseApi
{
    /// <summary>Reads the Lease.</summary>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The Lease, or <see langword="null"/> when there is none.</returns>
    Task<KubernetesLease?> GetAsync(CancellationToken ct);

    /// <summary>Creates the Lease, or replaces it when it has a version; fails when someone changed it in between.</summary>
    /// <param name="lease">What to write.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>Whether it was written.</returns>
    Task<bool> WriteAsync(KubernetesLease lease, CancellationToken ct);
}

/// <summary>Reads and writes the Lease through the API server.</summary>
/// <param name="http">Reaches the API server with the credentials of the controller.</param>
/// <param name="namespaceName">The namespace of the Lease.</param>
/// <param name="name">Its name.</param>
internal sealed class HttpLeaseApi(HttpClient http, string namespaceName, string name) : ILeaseApi
{
    private string Collection => $"apis/coordination.k8s.io/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/leases";

    /// <inheritdoc />
    public async Task<KubernetesLease?> GetAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync($"{Collection}/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(LeaseJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> WriteAsync(KubernetesLease lease, CancellationToken ct)
    {
        using var response = lease.Metadata.ResourceVersion is null
            ? await http.PostAsJsonAsync(Collection, lease, LeaseJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false)
            : await http.PutAsJsonAsync($"{Collection}/{Uri.EscapeDataString(name)}", lease, LeaseJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false);
        if (response.StatusCode is System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}

/// <summary>
/// Chooses one replica to send alerts with a Kubernetes Lease: the replica that holds it renews it every third of its duration, and another one takes it over when the holder has been silent for the whole duration.
/// A replica that cannot reach the API server stops being the leader when its own lease would have run out, so two replicas never lead for long at once and a split costs at worst a repeated alert.
/// </summary>
/// <param name="api">Reads and writes the Lease.</param>
/// <param name="name">The name of the Lease.</param>
/// <param name="identity">Who this replica is.</param>
/// <param name="duration">How long a silent holder keeps the Lease.</param>
/// <param name="time">Clock for the renewals.</param>
/// <param name="logger">Receives what could not be done.</param>
internal sealed class LeaseLeaderElection(ILeaseApi api, string name, string identity, TimeSpan duration, TimeProvider time, ILogger<LeaseLeaderElection> logger) : BackgroundService, ILeaderElection
{
    private long _leaderUntil;

    /// <inheritdoc />
    public bool IsLeader => time.GetUtcNow().UtcTicks < Interlocked.Read(ref _leaderUntil);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(duration / 3);
        do
        {
            await ElectAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Looks at the Lease and renews, takes or leaves it.</summary>
    /// <param name="ct">Cancels the requests.</param>
    /// <returns>A task that completes when the look is done.</returns>
    public async Task ElectAsync(CancellationToken ct)
    {
        try
        {
            var now = time.GetUtcNow();
            var lease = await api.GetAsync(ct).ConfigureAwait(false);
            var mine = new LeaseSpec(identity, (int)Math.Ceiling(duration.TotalSeconds), lease?.Spec.HolderIdentity == identity ? lease.Spec.AcquireTime : MicroTime(now), MicroTime(now));
            var wanted = lease is null
                ? new KubernetesLease(new LeaseMetadata(name), mine)
                : lease.Spec.HolderIdentity == identity || Expired(lease, now) ? lease with { Spec = mine } : null;

            if (wanted is not null && await api.WriteAsync(wanted, ct).ConfigureAwait(false))
            {
                Interlocked.Exchange(ref _leaderUntil, (now + duration).UtcTicks);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Cannot reach the lease for sending alerts: {Reason}", ex.Message);
        }
    }

    private bool Expired(KubernetesLease lease, DateTimeOffset now) =>
        !DateTimeOffset.TryParse(lease.Spec.RenewTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var renewed)
        || now - renewed > TimeSpan.FromSeconds(Math.Max(lease.Spec.LeaseDurationSeconds, 1));

    private static string MicroTime(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
