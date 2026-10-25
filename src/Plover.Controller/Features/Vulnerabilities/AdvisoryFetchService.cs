using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Vulnerabilities;

/// <summary>
/// Downloads the advisories of the configured public sources when the controller has started and then at an interval. A source that cannot be fetched
/// keeps what it sent last time, and the others are not held up by it.
/// </summary>
/// <param name="fetcher">Downloads one source.</param>
/// <param name="store">Holds the advisories.</param>
/// <param name="options">Provides the sources and the intervals.</param>
/// <param name="time">Clock for the interval and the time limit.</param>
/// <param name="logger">Receives what could not be fetched.</param>
internal sealed class AdvisoryFetchService(
    AdvisoryFetcher fetcher, AdvisoryStore store, VulnerabilitiesOptions options, TimeProvider time, ILogger<AdvisoryFetchService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yields first, so the controller finishes starting without waiting for the network.
        await Task.Yield();
        try
        {
            using var timer = new PeriodicTimer(options.FetchInterval, time);
            do
            {
                await FetchAllAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    /// <summary>Fetches every source once. Never throws for a source that fails.</summary>
    /// <param name="ct">Stops fetching.</param>
    /// <returns>A task that completes when every source has been tried.</returns>
    public async Task FetchAllAsync(CancellationToken ct)
    {
        foreach (var source in options.Sources)
        {
            using var timeout = new CancellationTokenSource(options.FetchTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                var advisories = await fetcher.FetchAsync(source, linked.Token).ConfigureAwait(false);
                store.SetFetched(source.Url.AbsoluteUri, source.Kind == AdvisorySource.OsvKind ? 0 : 1, advisories);
                logger.LogInformation("Fetched {Count} advisories from {Host}", advisories.Count, source.Url.Host);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or IOException
                or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Only the host is logged: an address can carry a key.
                logger.LogWarning("Advisories could not be fetched from {Host}: {Reason}. What was fetched before stays in force.", source.Url.Host, ex.Message);
            }
        }
    }
}
