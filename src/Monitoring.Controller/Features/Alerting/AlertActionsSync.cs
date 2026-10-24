using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>
/// Copies what operators did (silences, acknowledgements) from the other replicas now and then, so a silence made on the page of one replica holds on all of them, and a replica
/// that restarted learns it again. The latest change of each wins, so it does not matter in which order or how often the replicas ask each other.
/// </summary>
/// <param name="http">Sends the requests.</param>
/// <param name="actions">Takes over what the peers say.</param>
/// <param name="options">Provides the peers and the interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives the peers that cannot be reached.</param>
internal sealed class AlertActionsSync(HttpClient http, AlertActions actions, AlertingOptions options, TimeProvider time, ILogger logger) : BackgroundService
{
    private const int MaxBytes = 1024 * 1024;
    private readonly HashSet<Uri> _unreachable = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            using var timer = new PeriodicTimer(options.PeerInterval, time);
            do
            {
                await SyncAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    /// <summary>Asks every peer once. Never throws for a peer that cannot be reached.</summary>
    /// <param name="ct">Stops asking.</param>
    /// <returns>A task that completes when every peer has been asked.</returns>
    public async Task SyncAsync(CancellationToken ct)
    {
        foreach (var peer in options.Peers)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(peer, "/api/actions"));
                if (options.PeerToken is { Length: > 0 } token)
                {
                    request.Headers.Authorization = new("Bearer", token);
                }

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxBytes)
                {
                    throw new InvalidDataException("The answer is too large.");
                }

                await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var limited = new MemoryStream();
                var chunk = new byte[16384];
                int read;
                while ((read = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
                {
                    limited.Write(chunk, 0, read);
                    if (limited.Length > MaxBytes)
                    {
                        throw new InvalidDataException("The answer is too large.");
                    }
                }

                actions.Merge(JsonSerializer.Deserialize(limited.ToArray(), AlertActionsJsonContext.Default.ActionsExport) ?? new ActionsExport([], []));
                if (_unreachable.Remove(peer))
                {
                    logger.LogInformation("Replica {Host} can be reached again", peer.Host);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or JsonException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Said once until it works again, so a replica that is down does not fill the log.
                if (_unreachable.Add(peer))
                {
                    logger.LogWarning("The silences and acknowledgements of replica {Host} cannot be read: {Reason}", peer.Host, ex.Message);
                }
            }
        }
    }
}
