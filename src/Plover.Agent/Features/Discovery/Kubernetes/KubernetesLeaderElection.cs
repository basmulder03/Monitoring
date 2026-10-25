using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Agent.Features.Discovery.Kubernetes.Models;

namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>
/// Competes for a Lease with the other agents of the namespace. The holder renews it every interval; the others take it over once it has not changed
/// for the lease duration, measured on this machine's clock so a skewed clock elsewhere does not matter. A holder that cannot renew for the lease
/// duration stops the agent, because by then another agent may have taken over, and Kubernetes starts it again as a waiting one.
/// </summary>
/// <param name="api">The Kubernetes API.</param>
/// <param name="target">The namespace of the lease.</param>
/// <param name="options">Lease name and timing.</param>
/// <param name="identity">Who this agent is; the pod name.</param>
/// <param name="time">Clock for the waits.</param>
/// <param name="lifetime">Stops the agent when the lease is lost.</param>
/// <param name="logger">Receives changes of leadership and failures.</param>
internal sealed class KubernetesLeaderElection(
    ILeaseApi api,
    DiscoveryTarget target,
    LeaderElectionOptions options,
    string identity,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<KubernetesLeaderElection> logger) : BackgroundService, ILeadership
{
    private readonly TaskCompletionSource _elected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The renew time last seen on the lease and when this agent saw it change, for the others' expiry.</summary>
    private string? _observedRenew;
    private DateTimeOffset _observedAt;

    private DateTimeOffset _lastRenewed;
    private Lease? _held;

    /// <inheritdoc />
    public bool IsLeader => _elected.Task.IsCompleted;

    /// <inheritdoc />
    public Task WaitUntilLeaderAsync(CancellationToken ct) => _elected.Task.WaitAsync(ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.RenewInterval, time);
        do
        {
            await StepAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Gives the lease up when the agent stops normally, so a replacement does not wait for it to expire.</summary>
    /// <param name="cancellationToken">Gives up waiting.</param>
    /// <returns>A task that completes once the lease is released or could not be.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_held is { } held)
        {
            try
            {
                await api.ReplaceLeaseAsync(target.Namespace, held with { Spec = held.Spec with { HolderIdentity = null, LeaseDurationSeconds = 1 } }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or OperationCanceledException)
            {
                logger.LogDebug(ex, "The lease {Lease} could not be given up; it expires by itself.", options.LeaseName);
            }
        }
    }

    /// <summary>Looks at the lease once and takes or renews it when it can.</summary>
    /// <param name="ct">Cancels the requests.</param>
    /// <returns>A task that completes when the step is done.</returns>
    internal async Task StepAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        try
        {
            if (await TryHoldAsync(now, ct).ConfigureAwait(false) && !IsLeader)
            {
                logger.LogInformation("This agent ({Identity}) now serves the scope: it holds the lease {Lease}.", identity, options.LeaseName);
                _elected.TrySetResult();
            }
        }
        catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("The lease {Lease} could not be read or written: {Reason}", options.LeaseName, ex.Message);
        }

        if (IsLeader && now - _lastRenewed > options.LeaseDuration)
        {
            logger.LogError("The lease {Lease} could not be renewed for {Seconds} s; stopping, as another agent may have taken over.", options.LeaseName, options.LeaseDuration.TotalSeconds);
            lifetime.StopApplication();
        }
    }

    private async Task<bool> TryHoldAsync(DateTimeOffset now, CancellationToken ct)
    {
        var stamp = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
        var seconds = (int)Math.Ceiling(options.LeaseDuration.TotalSeconds);
        var lease = await api.GetLeaseAsync(target.Namespace, options.LeaseName, ct).ConfigureAwait(false);
        if (lease is null)
        {
            var created = new Lease(new LeaseMetadata(options.LeaseName), new LeaseSpec(identity, seconds, stamp, stamp, 0));
            return Held(await WriteAsync(() => api.CreateLeaseAsync(target.Namespace, created, ct)).ConfigureAwait(false), now);
        }

        var spec = lease.Spec;
        if (spec.RenewTime != _observedRenew)
        {
            (_observedRenew, _observedAt) = (spec.RenewTime, now);
        }

        var mine = spec.HolderIdentity == identity;
        var free = string.IsNullOrEmpty(spec.HolderIdentity) || now - _observedAt > TimeSpan.FromSeconds(spec.LeaseDurationSeconds ?? seconds);
        if (!mine && !free)
        {
            return false;
        }

        var next = mine
            ? spec with { LeaseDurationSeconds = seconds, RenewTime = stamp }
            : new LeaseSpec(identity, seconds, stamp, stamp, (spec.LeaseTransitions ?? 0) + 1);
        return Held(await WriteAsync(() => api.ReplaceLeaseAsync(target.Namespace, lease with { Spec = next }, ct)).ConfigureAwait(false), now);
    }

    /// <summary>Writes the lease; losing the race to another agent (409) just means this one does not hold it.</summary>
    private static async Task<Lease?> WriteAsync(Func<Task<Lease>> write)
    {
        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (KubernetesApiException ex) when (ex.StatusCode == 409)
        {
            return null;
        }
    }

    private bool Held(Lease? written, DateTimeOffset now)
    {
        _held = written;
        if (written is not null)
        {
            _lastRenewed = now;
            (_observedRenew, _observedAt) = (written.Spec.RenewTime, now);
        }

        return written is not null;
    }
}
