using Plover.Agent.Features.MetricsHistory;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.MetricsHistory;

public class RingMetricsHistoryTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private static MetricSeries Value(string name, double value, string? route = null) =>
        new(name, route is null ? NoLabels : new Dictionary<string, string> { ["route"] = route }, value);

    private static RingMetricsHistory History(int samples = 3, int series = 200) =>
        new(new MetricsHistoryOptions { Samples = samples, MaxSeriesPerInstance = series });

    [Fact]
    public void Get_returns_the_values_oldest_first()
    {
        // Arrange
        var history = History();
        history.Record("a", [Value("m", 1)]);
        history.Record("a", [Value("m", 2)]);

        // Act
        var result = history.Get("a", intervalSeconds: 15)!;

        // Assert
        result.IntervalSeconds.ShouldBe(15);
        result.Series.Single().Values.ShouldBe([1, 2]);
    }

    [Fact]
    public void Get_keeps_only_the_latest_values_once_the_ring_is_full()
    {
        // Arrange
        var history = History(samples: 3);
        foreach (var value in new[] { 1, 2, 3, 4, 5 })
        {
            history.Record("a", [Value("m", value)]);
        }

        // Act
        var values = history.Get("a", 15)!.Series.Single().Values;

        // Assert
        values.ShouldBe([3, 4, 5]);
    }

    [Fact]
    public void Get_keeps_series_with_different_tags_apart()
    {
        // Arrange
        var history = History();
        history.Record("a", [Value("m", 1, "/x"), Value("m", 2, "/y")]);

        // Act
        var series = history.Get("a", 15)!.Series;

        // Assert
        series.Count.ShouldBe(2);
    }

    [Fact]
    public void Record_ignores_series_beyond_the_limit_per_instance()
    {
        // Arrange
        var history = History(series: 2);

        // Act
        history.Record("a", [Value("m1", 1), Value("m2", 1), Value("m3", 1)]);

        // Assert
        history.Get("a", 15)!.Series.Select(s => s.Name).ShouldBe(["m1", "m2"]);
    }

    [Fact]
    public void Forget_drops_an_instance_and_Get_knows_nothing_of_it_afterwards()
    {
        // Arrange
        var history = History();
        history.Record("a", [Value("m", 1)]);

        // Act
        history.Forget("a");

        // Assert
        history.Get("a", 15).ShouldBeNull();
    }
}
