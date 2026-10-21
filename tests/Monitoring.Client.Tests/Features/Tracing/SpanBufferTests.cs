using Monitoring.Client.Features.Tracing;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Tracing;

public class SpanBufferTests
{
    private static SpanReport Span(long sequence) =>
        new(sequence, "t", "s", null, "op", "Client", DateTimeOffset.UnixEpoch, 1, false, new Dictionary<string, string>());

    [Fact]
    public void ReadAfter_returns_the_spans_newer_than_the_cursor_oldest_first()
    {
        // Arrange
        var buffer = new SpanBuffer(10);
        for (var i = 0; i < 4; i++)
        {
            buffer.Add(Span);
        }

        // Act
        var (spans, cursor) = buffer.ReadAfter(2);

        // Assert
        spans.Select(s => s.Sequence).ShouldBe([3, 4]);
        cursor.ShouldBe(4);
    }

    [Fact]
    public void ReadAfter_returns_nothing_new_when_the_cursor_is_current()
    {
        // Arrange
        var buffer = new SpanBuffer(10);
        buffer.Add(Span);

        // Act
        var (spans, cursor) = buffer.ReadAfter(1);

        // Assert
        spans.ShouldBeEmpty();
        cursor.ShouldBe(1);
    }

    [Fact]
    public void Add_overwrites_the_oldest_span_when_full_and_counts_what_was_pushed_out()
    {
        // Arrange
        var buffer = new SpanBuffer(3);

        // Act
        for (var i = 0; i < 5; i++)
        {
            buffer.Add(Span);
        }

        // Assert
        buffer.ReadAfter(0).Spans.Select(s => s.Sequence).ShouldBe([3, 4, 5]);
        buffer.Dropped.ShouldBe(2);
    }
}
