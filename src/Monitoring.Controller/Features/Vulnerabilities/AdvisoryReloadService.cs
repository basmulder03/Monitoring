using Microsoft.Extensions.Hosting;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Loads the advisories when the controller starts and looks for changes at an interval.</summary>
/// <param name="store">Holds the advisories.</param>
/// <param name="options">Provides the interval.</param>
/// <param name="time">Clock for the interval.</param>
internal sealed class AdvisoryReloadService(AdvisoryStore store, VulnerabilitiesOptions options, TimeProvider time) : BackgroundService
{
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        store.Refresh();
        _timer = new PeriodicTimer(options.PollInterval, time);
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
        if (options.Advisories is null)
        {
            return;
        }

        try
        {
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                store.Refresh();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }
}
