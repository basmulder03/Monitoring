using System.Diagnostics.Metrics;
using Plover.Client.Features.LocalServer;

namespace Plover.Client.Features.Queues;

/// <summary>Reports the gauges <c>plover.queue.depth</c>, <c>plover.queue.consumers</c> and <c>plover.queue.dead_letters</c>, tagged with the name of the queue.</summary>
/// <remarks>A queue that cannot be read is left out for that reading, so a gap in the series is the sign; reading it never takes the application down.</remarks>
internal sealed class QueueMeter : IDisposable
{
    /// <summary>The name of the meter, which carries the instruments.</summary>
    public const string MeterName = "Plover.Client.Queues";

    private readonly Meter _meter = new(MeterName);
    private readonly IReadOnlyList<QueueWatch> _watches;

    /// <summary>Creates the instruments.</summary>
    /// <param name="options">Lists the queues to watch.</param>
    public QueueMeter(PloverOptions options)
    {
        _watches = [.. options.Queues];
        _meter.CreateObservableGauge("plover.queue.depth", () => Observe(stats => stats.Depth), description: "Messages waiting in the queue.");
        _meter.CreateObservableGauge("plover.queue.consumers", () => Observe(stats => stats.Consumers), description: "Consumers attached to the queue.");
        _meter.CreateObservableGauge("plover.queue.dead_letters", () => Observe(stats => stats.DeadLetters), description: "Messages in the dead-letter queue.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> Observe(Func<QueueStats, long?> pick)
    {
        foreach (var watch in _watches)
        {
            QueueStats? stats = null;
            try
            {
                stats = watch.Read();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The broker cannot be asked right now; the next reading tries again.
            }

            if (stats is not null && pick(stats) is { } value)
            {
                yield return new Measurement<long>(value, new KeyValuePair<string, object?>("queue", watch.Name));
            }
        }
    }
}
