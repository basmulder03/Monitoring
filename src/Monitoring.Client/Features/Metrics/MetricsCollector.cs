using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Metrics;

/// <summary>
/// Listens to every meter of the process, including the runtime's own, and keeps the latest value per instrument and combination of tags.
/// </summary>
/// <remarks>
/// Counters keep running totals and histograms a count and a sum, so what is read is cumulative and a missed pull loses nothing.
/// Memory is bounded: once <see cref="MonitoringOptions.MaxMetricSeries"/> series exist, new ones are ignored. All members are safe to call from any thread.
/// </remarks>
/// <param name="options">Which meters to leave out and how many series to keep.</param>
internal sealed class MetricsCollector(MonitoringOptions options) : IHostedService, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SeriesKey, double> _values = [];
    private MeterListener? _listener;

    /// <summary>Starts listening, which also reports the instruments that already exist.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || _listener is not null)
        {
            return Task.CompletedTask;
        }

        var listener = new MeterListener { InstrumentPublished = OnPublished };
        listener.SetMeasurementEventCallback<byte>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<short>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<float>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<decimal>((i, v, t, _) => Record(i, (double)v, t));
        listener.Start();
        _listener = listener;
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

    /// <summary>Reads the latest value of every series, after asking the observable instruments for theirs.</summary>
    /// <returns>The series, ordered by name and tags so the answer is stable.</returns>
    public MetricsReport Read()
    {
        _listener?.RecordObservableInstruments();

        lock (_gate)
        {
            return new MetricsReport([.. _values
                .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.TagText, StringComparer.Ordinal)
                .Select(pair => new MetricSeries(pair.Key.Name, pair.Key.Tags, pair.Value))]);
        }
    }

    private void OnPublished(Instrument instrument, MeterListener listener)
    {
        if (!IsDenied(instrument.Name))
        {
            listener.EnableMeasurementEvents(instrument);
        }
    }

    private bool IsDenied(string name) => options.MetricsDeny.Any(pattern =>
        pattern.EndsWith('*') ? name.StartsWith(pattern[..^1], StringComparison.Ordinal) : name == pattern);

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var key = SeriesKey.Of(instrument.Name, tags);
        var kind = Kind.Of(instrument);

        lock (_gate)
        {
            switch (kind)
            {
                case Kind.Total:
                    Add(key, value);
                    break;

                case Kind.Distribution:
                    Add(key.Suffixed(".count"), 1);
                    Add(key.Suffixed(".sum"), value);
                    break;

                default:
                    Set(key, value);
                    break;
            }
        }
    }

    private void Add(SeriesKey key, double amount)
    {
        if (_values.TryGetValue(key, out var current))
        {
            _values[key] = current + amount;
        }
        else if (_values.Count < options.MaxMetricSeries)
        {
            _values[key] = amount;
        }
    }

    private void Set(SeriesKey key, double value)
    {
        if (_values.ContainsKey(key) || _values.Count < options.MaxMetricSeries)
        {
            _values[key] = value;
        }
    }

    /// <summary>How the measurements of an instrument combine into one value.</summary>
    private static class Kind
    {
        /// <summary>Each measurement is a change to add up (counters).</summary>
        public const int Total = 0;

        /// <summary>Each measurement is a sample whose count and sum are kept (histograms).</summary>
        public const int Distribution = 1;

        /// <summary>Each measurement is the value now (gauges and observable instruments).</summary>
        public const int Latest = 2;

        public static int Of(Instrument instrument) => instrument.GetType().Name switch
        {
            var name when name.StartsWith("Counter`", StringComparison.Ordinal) => Total,
            var name when name.StartsWith("UpDownCounter`", StringComparison.Ordinal) => Total,
            var name when name.StartsWith("Histogram`", StringComparison.Ordinal) => Distribution,
            _ => Latest,
        };
    }
}
