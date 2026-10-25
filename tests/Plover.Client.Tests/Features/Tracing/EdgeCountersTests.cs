using Plover.Client.Features.Tracing;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Features.Tracing;

public class EdgeCountersTests
{
    [Fact]
    public void Read_counts_calls_errors_and_time_per_dependency()
    {
        // Arrange
        var counters = new EdgeCounters(10);

        // Act
        counters.Record("HTTP", "shop:80", 10, error: false);
        counters.Record("HTTP", "shop:80", 30, error: true);
        counters.Record("DB", "sql://db/orders", 5, error: false);
        var report = counters.Read();

        // Assert
        var http = report.Edges.Single(e => e.Type == "HTTP");
        http.Target.ShouldBe("shop:80");
        http.Calls.ShouldBe(2);
        http.Errors.ShouldBe(1);
        http.DurationSumMs.ShouldBe(40);
        report.Edges.Select(e => e.Type).ShouldBe(["DB", "HTTP"]);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(5.1, 1)]
    [InlineData(100, 4)]
    [InlineData(5000, 9)]
    [InlineData(5001, 10)]
    public void Read_puts_each_call_in_the_bucket_of_its_duration(double durationMs, int bucket)
    {
        // Arrange
        var counters = new EdgeCounters(10);

        // Act
        counters.Record("HTTP", "shop:80", durationMs, error: false);
        var buckets = counters.Read().Edges.Single().Buckets;

        // Assert
        buckets.Count.ShouldBe(EdgesReport.BucketBoundsMs.Count + 1);
        buckets[bucket].ShouldBe(1);
        buckets.Sum().ShouldBe(1);
    }

    [Fact]
    public void Record_ignores_new_dependencies_beyond_the_limit_but_keeps_counting_the_known_ones()
    {
        // Arrange
        var counters = new EdgeCounters(1);
        counters.Record("HTTP", "a:80", 1, false);

        // Act
        counters.Record("HTTP", "b:80", 1, false);
        counters.Record("HTTP", "a:80", 1, false);
        var report = counters.Read();

        // Assert
        report.Edges.Single().Target.ShouldBe("a:80");
        report.Edges.Single().Calls.ShouldBe(2);
    }
}
