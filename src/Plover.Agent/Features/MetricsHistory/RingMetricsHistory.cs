using Plover.Protocol.Reports;

namespace Plover.Agent.Features.MetricsHistory;

/// <summary>Default <see cref="IMetricsHistory"/>: a ring of values per series, under one lock.</summary>
/// <param name="options">Provides the sizes.</param>
internal sealed class RingMetricsHistory(MetricsHistoryOptions options) : IMetricsHistory
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, Series>> _instances = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Record(string instanceId, IReadOnlyList<MetricSeries> series)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var kept))
            {
                _instances[instanceId] = kept = new Dictionary<string, Series>(StringComparer.Ordinal);
            }

            foreach (var value in series)
            {
                var key = KeyOf(value);
                if (!kept.TryGetValue(key, out var ring))
                {
                    if (kept.Count >= options.MaxSeriesPerInstance)
                    {
                        continue;
                    }

                    kept[key] = ring = new Series(value.Name, value.Labels, options.Samples);
                }

                ring.Add(value.Value);
            }
        }
    }

    /// <inheritdoc />
    public void Forget(string instanceId)
    {
        lock (_gate)
        {
            _instances.Remove(instanceId);
        }
    }

    /// <inheritdoc />
    public MetricHistory? Get(string instanceId, int intervalSeconds)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out var kept)
                ? new MetricHistory(intervalSeconds, [.. kept.Values.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => new SeriesHistory(s.Name, s.Labels, s.Values()))])
                : null;
        }
    }

    private static string KeyOf(MetricSeries series) =>
        series.Labels.Count == 0 ? series.Name : $"{series.Name}|{string.Join(',', series.Labels.OrderBy(l => l.Key, StringComparer.Ordinal).Select(l => $"{l.Key}={l.Value}"))}";

    /// <summary>The last values of one series, oldest overwritten first.</summary>
    private sealed class Series(string name, IReadOnlyDictionary<string, string> labels, int capacity)
    {
        private readonly double[] _values = new double[capacity];
        private int _next;
        private int _count;

        public string Name { get; } = name;

        public IReadOnlyDictionary<string, string> Labels { get; } = labels;

        public void Add(double value)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            _count = Math.Min(_count + 1, _values.Length);
        }

        /// <summary>The values, oldest first.</summary>
        public double[] Values()
        {
            var ordered = new double[_count];
            var start = (_next - _count + _values.Length) % _values.Length;
            for (var i = 0; i < _count; i++)
            {
                ordered[i] = _values[(start + i) % _values.Length];
            }

            return ordered;
        }
    }
}
