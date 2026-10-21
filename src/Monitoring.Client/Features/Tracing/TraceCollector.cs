using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Tracing;

/// <summary>
/// Listens to the activities of the application, counts its calls to dependencies and keeps its latest spans.
/// </summary>
/// <remarks>
/// Only the sources named in <see cref="MonitoringOptions.TraceSources"/> are listened to, which keeps the cost small. W3C trace context is
/// passed on by the platform itself (an HTTP client adds <c>traceparent</c> while an activity runs), so nothing here touches requests.
/// What is kept is cleaned first: see <see cref="SpanSanitizer"/>.
/// </remarks>
/// <param name="options">Which sources to listen to, and whether to run.</param>
/// <param name="edges">Receives the calls to dependencies.</param>
/// <param name="spans">Receives the spans.</param>
internal sealed class TraceCollector(MonitoringOptions options, EdgeCounters edges, SpanBuffer spans) : IHostedService, IDisposable
{
    private static readonly string[] DetailKeys =
    [
        "http.request.method", "http.response.status_code", "server.address", "server.port", "error.type",
        "db.system", "db.system.name", "db.operation", "db.operation.name", "db.namespace", "messaging.system", "messaging.destination.name",
    ];

    private ActivityListener? _listener;

    /// <summary>Starts listening.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || _listener is not null)
        {
            return Task.CompletedTask;
        }

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
    }

    private void OnStopped(Activity activity)
    {
        var error = activity.Status == ActivityStatusCode.Error
            || activity.GetTagItem("error.type") is not null
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
    private static string Name(Activity activity) => SpanSanitizer.Url(activity.DisplayName);

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
