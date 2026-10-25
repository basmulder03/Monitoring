using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.ChangeMarkers;

/// <summary>Looks for new versions at a fixed interval.</summary>
/// <param name="tracker">Records the changes.</param>
/// <param name="options">Provides the interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives failures.</param>
internal sealed class ChangeSampleService(ChangeTracker tracker, ChangeMarkersOptions options, TimeProvider time, ILogger<ChangeSampleService> logger)
    : BackgroundService
{
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here, not in ExecuteAsync, so the interval counts from the start of the service.
        _timer = new PeriodicTimer(options.SampleInterval, time);
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
                    tracker.Sample();
                }
                catch (Exception ex)
                {
                    // A failed sample must not end the markers; the next tick tries again.
                    logger.LogError(ex, "Looking for new versions failed");
                }
            }
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }
}
