using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>
/// Streams the overview to one page as Server-Sent Events: the current overview at once, then a new one whenever it changes.
/// </summary>
/// <param name="model">Builds the overview.</param>
/// <param name="options">Provides the push and keep-alive intervals.</param>
/// <param name="time">Clock for the intervals.</param>
internal sealed class OverviewStream(DashboardModel model, DashboardOptions options, TimeProvider time)
{
    /// <summary>Writes events to the response until the page goes away.</summary>
    /// <param name="response">The response to stream into.</param>
    /// <param name="ct">Cancelled when the page disconnects or the controller shuts down.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    public async Task StreamAsync(HttpResponse response, CancellationToken ct)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        // Created before anything is sent, so a page that has received its first event knows the next check is already scheduled.
        using var timer = new PeriodicTimer(options.PushInterval, time);
        string? last = null;
        var silentSince = time.GetTimestamp();

        try
        {
            do
            {
                var json = JsonSerializer.Serialize(model.BuildOverview(), DashboardJsonContext.Default.Overview);
                if (json != last)
                {
                    last = json;
                    await response.WriteAsync($"event: overview\ndata: {json}\n\n", ct).ConfigureAwait(false);
                    await response.Body.FlushAsync(ct).ConfigureAwait(false);
                    silentSince = time.GetTimestamp();
                }
                else if (time.GetElapsedTime(silentSince) >= options.KeepAliveInterval)
                {
                    await response.WriteAsync(": keep-alive\n\n", ct).ConfigureAwait(false);
                    await response.Body.FlushAsync(ct).ConfigureAwait(false);
                    silentSince = time.GetTimestamp();
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The page went away or the controller is shutting down.
        }
    }
}
