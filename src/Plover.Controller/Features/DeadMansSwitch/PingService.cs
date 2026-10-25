using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.DeadMansSwitch;

/// <summary>Background service that pings the external watchdog at startup and then on a fixed interval, so it can alert when all replicas have gone quiet.</summary>
/// <param name="http">Sends the pings.</param>
/// <param name="options">Provides the address and the interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives ping failures.</param>
internal sealed class PingService(HttpClient http, DeadMansSwitchOptions options, TimeProvider time, ILogger<PingService> logger) : BackgroundService
{
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here, so the interval starts counting when the service starts and not when its thread gets going.
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
                await PingAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    private async Task PingAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(options.Url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The watchdog answered {Status} to the ping", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // A watchdog that cannot be reached must never take the controller down; it alerts on its own side.
            logger.LogWarning(ex, "Pinging the watchdog failed");
        }
    }
}
