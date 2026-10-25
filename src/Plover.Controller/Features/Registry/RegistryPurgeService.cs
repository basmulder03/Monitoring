using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Registry;

/// <summary>Background service that periodically drops Gone instances once their retention has passed.</summary>
/// <param name="registry">The registry to purge.</param>
/// <param name="options">Provides the purge interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives purge failures.</param>
internal sealed class RegistryPurgeService(
    IScopeRegistry registry,
    RegistryOptions options,
    TimeProvider time,
    ILogger<RegistryPurgeService> logger) : BackgroundService
{
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here rather than in ExecuteAsync, which the host may run on another thread: the interval
        // starts counting when the service starts, not when that thread gets going.
        _timer = new PeriodicTimer(options.PurgeInterval, time);
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
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    registry.PurgeExpired();
                }
                catch (Exception ex)
                {
                    // A failed purge must not end housekeeping; the next tick tries again.
                    logger.LogError(ex, "Purging expired instances failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }
}
