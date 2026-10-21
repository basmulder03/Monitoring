using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;
using Monitoring.Sidecar.Features.LibraryLink;

namespace Monitoring.Sidecar.Features.Probing;

/// <summary>Asks the application for its description and health at a fixed interval and keeps the latest answers.</summary>
/// <param name="link">The way to the application.</param>
/// <param name="options">Provides the interval.</param>
/// <param name="time">Clock for the interval and for the time the application was last reached.</param>
/// <param name="logger">Receives unexpected failures.</param>
internal sealed class ProbeService(ILibraryLink link, ProbeOptions options, TimeProvider time, ILogger<ProbeService> logger)
    : BackgroundService, IProbeResults
{
    private static readonly HealthReport NotReached = new(
        HealthState.Unhealthy,
        [new HealthCheckResult("application", HealthState.Unhealthy, "The application cannot be reached.")]);

    private readonly Lock _gate = new();
    private readonly TaskCompletionSource<InstanceDescription> _introduced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApplicationStatus _current = new(null, NotReached, null);
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public ApplicationStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public async Task<InstanceDescription> WaitForDescriptionAsync(CancellationToken ct)
    {
        await _introduced.Task.WaitAsync(ct).ConfigureAwait(false);

        // The current description, not the first one: the application may have restarted with a new version since.
        return Current.Description!;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here rather than in ExecuteAsync, which the host may run on another thread: the interval
        // starts counting when the service starts, not when that thread gets going.
        _timer = new PeriodicTimer(options.Interval, time);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                try
                {
                    await ProbeAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A probe that fails in an unexpected way must not end probing.
                    logger.LogError(ex, "Probing the application failed");
                }
            }
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    private async Task ProbeAsync(CancellationToken ct)
    {
        var description = await link.GetDescriptionAsync(ct).ConfigureAwait(false);
        var health = await link.GetHealthAsync(ct).ConfigureAwait(false);

        lock (_gate)
        {
            // An application that is briefly unreachable keeps the identity it had.
            var known = description ?? _current.Description;
            _current = new ApplicationStatus(
                known,
                health ?? NotReached,
                description is not null || health is not null ? time.GetUtcNow() : _current.LastReached);

            if (known is not null)
            {
                _introduced.TrySetResult(known);
            }
        }
    }
}
