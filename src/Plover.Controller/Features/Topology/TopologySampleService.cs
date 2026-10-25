using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Topology;

/// <summary>Updates the dependency graph at a fixed interval.</summary>
/// <param name="tracker">Builds the graph.</param>
/// <param name="options">Provides the interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives failures.</param>
internal sealed class TopologySampleService(TopologyTracker tracker, TopologyOptions options, TimeProvider time, ILogger<TopologySampleService> logger)
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
                    // A failed sample must not end the graph; the next tick tries again.
                    logger.LogError(ex, "Updating the dependency graph failed");
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
