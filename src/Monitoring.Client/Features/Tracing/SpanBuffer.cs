using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Keeps the most recent spans in a ring, so the agent can fetch slow or failed ones when someone opens an instance. Nothing is stored anywhere else.</summary>
/// <param name="capacity">How many spans are kept; older ones are overwritten.</param>
internal sealed class SpanBuffer(int capacity)
{
    private readonly Lock _gate = new();
    private readonly SpanReport?[] _ring = new SpanReport?[capacity];
    private long _last;

    /// <summary>How many spans were pushed out before anyone read them, which shows the buffer is too small for the traffic.</summary>
    public long Dropped { get; private set; }

    /// <summary>How many spans are kept now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return (int)Math.Min(_last, capacity);
            }
        }
    }

    /// <summary>Adds a span, numbering it.</summary>
    /// <param name="build">Creates the span from the number it was given.</param>
    public void Add(Func<long, SpanReport> build)
    {
        lock (_gate)
        {
            var sequence = ++_last;
            var slot = (int)((sequence - 1) % capacity);
            if (_ring[slot] is not null)
            {
                Dropped++;
            }

            _ring[slot] = build(sequence);
        }
    }

    /// <summary>Reads the spans newer than a cursor.</summary>
    /// <param name="cursor">The highest sequence number the reader has seen; 0 for none.</param>
    /// <returns>The spans, oldest first, and the cursor to use next.</returns>
    public (IReadOnlyList<SpanReport> Spans, long Cursor) ReadAfter(long cursor)
    {
        lock (_gate)
        {
            var spans = _ring.OfType<SpanReport>().Where(span => span.Sequence > cursor).OrderBy(span => span.Sequence).ToList();
            return (spans, _last);
        }
    }
}
