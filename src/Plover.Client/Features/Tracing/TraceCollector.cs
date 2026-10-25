using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Plover.Client.Features.LocalServer;
using Plover.Protocol.Reports;

namespace Plover.Client.Features.Tracing;

/// <summary>
/// Listens to the activities of the application, counts its calls to dependencies and keeps its latest spans.
/// </summary>
/// <remarks>
/// Only the sources named in <see cref="PloverOptions.TraceSources"/> are listened to, which keeps the cost small. W3C trace context is
/// passed on by the platform itself (an HTTP client adds <c>traceparent</c> while an activity runs), so nothing here touches requests.
/// What is kept is cleaned first: see <see cref="SpanSanitizer"/>.
/// </remarks>
/// <param name="options">Which sources to listen to, and whether to run.</param>
/// <param name="edges">Receives the calls to dependencies.</param>
/// <param name="spans">Receives the spans.</param>
internal sealed class TraceCollector(PloverOptions options, EdgeCounters edges, SpanBuffer spans) : IHostedService, IDisposable
{
    private static readonly string[] DetailKeys =
    [
        "http.request.method", "http.response.status_code", "server.address", "server.port", "error.type",
        "db.system", "db.system.name", "db.operation", "db.operation.name", "db.namespace", "messaging.system", "messaging.destination.name",
    ];

    private ActivityListener? _listener;
    private Meter? _meter;

    /// <summary>Starts listening.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || _listener is not null)
        {
            return Task.CompletedTask;
        }

        // What the library tells about itself: whether it keeps up. A rising number of dropped spans says the buffer is too small for the traffic.
        _meter = new Meter("Plover.Client.Tracing");
        _meter.CreateObservableGauge("plover.client.spans_buffered", () => spans.Count, description: "Spans kept in memory for drill-down.");
        _meter.CreateObservableCounter("plover.client.spans_dropped", () => spans.Dropped, description: "Spans overwritten before anyone read them.");
        _meter.CreateObservableGauge("plover.client.edges", () => edges.Count, description: "Dependencies counted; calls to further ones are ignored at the limit.");

        var sources = options.TraceSources.ToHashSet(StringComparer.Ordinal);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => sources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    /// <summary>Stops listening.</summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
        _meter?.Dispose();
        _meter = null;
    }

    private void OnStopped(Activity activity)
    {
        // Newer code sets the status of the activity; older code, such as Npgsql, says it with a tag and an exception event instead.
        var error = activity.Status == ActivityStatusCode.Error
            || activity.GetTagItem("error.type") is not null
            || string.Equals(activity.GetTagItem("otel.status_code")?.ToString(), "ERROR", StringComparison.OrdinalIgnoreCase)
            || activity.Events.Any(e => e.Name == "exception")
            || StatusCodeOf(activity) >= 500;
        var duration = activity.Duration.TotalMilliseconds;

        if (EdgeClassifier.TryClassify(activity, out var type, out var target))
        {
            edges.Record(type, target, duration, error);
        }

        spans.Add(sequence => new SpanReport(
            sequence,
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            Name(activity),
            activity.Kind.ToString(),
            activity.StartTimeUtc,
            duration,
            error,
            DetailsOf(activity)));
    }

    private static int StatusCodeOf(Activity activity) =>
        int.TryParse(activity.GetTagItem("http.response.status_code")?.ToString(), out var code) ? code : 0;

    /// <summary>Names a span without anything a person typed: an HTTP operation is named by method and route, never by address.</summary>
    private static string Name(Activity activity)
    {
        // Some providers do not name their operations at all (Npgsql says UNKNOWN); the kind of database and what it was asked to do is more use.
        if (activity.DisplayName is "" or "UNKNOWN" && (activity.GetTagItem("db.system.name") ?? activity.GetTagItem("db.system"))?.ToString() is { } database)
        {
            var statement = (activity.GetTagItem("db.query.text") ?? activity.GetTagItem("db.statement"))?.ToString();
            var verb = statement?.TrimStart().Split([' ', '\n', '\t', '('], 2)[0].ToLowerInvariant();
            return string.IsNullOrEmpty(verb) ? database : $"{database} {verb}";
        }

        return SpanSanitizer.Url(activity.DisplayName);
    }

    private static Dictionary<string, string> DetailsOf(Activity activity)
    {
        var details = new Dictionary<string, string>();
        foreach (var key in DetailKeys)
        {
            if (activity.GetTagItem(key)?.ToString() is { } value)
            {
                details[key] = value;
            }
        }

        if ((activity.GetTagItem("url.full")?.ToString() ?? activity.GetTagItem("http.url")?.ToString()) is { } url)
        {
            details["url"] = SpanSanitizer.Url(url);
        }

        if ((activity.GetTagItem("db.query.text")?.ToString() ?? activity.GetTagItem("db.statement")?.ToString()) is { } statement)
        {
            details["db.statement"] = SpanSanitizer.Sql(statement);
        }

        return details;
    }
}
