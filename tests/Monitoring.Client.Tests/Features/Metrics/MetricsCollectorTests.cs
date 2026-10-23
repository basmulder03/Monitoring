using System.Diagnostics.Metrics;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Metrics;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Metrics;

[Collection("Meters")]
public class MetricsCollectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_adds_up_the_measurements_of_a_counter()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var counter = fixture.Meter.CreateCounter<long>("orders.placed");

        // Act
        counter.Add(2);
        counter.Add(3);

        // Assert
        fixture.Series("orders.placed").Single().Value.ShouldBe(5);
    }

    [Fact]
    public async Task Read_keeps_a_series_per_combination_of_tags()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var counter = fixture.Meter.CreateCounter<int>("orders.placed");

        // Act
        counter.Add(1, new KeyValuePair<string, object?>("channel", "web"));
        counter.Add(4, new KeyValuePair<string, object?>("channel", "app"));
        counter.Add(1, new KeyValuePair<string, object?>("channel", "web"));

        // Assert
        var series = fixture.Series("orders.placed");
        series.Single(s => s.Labels["channel"] == "web").Value.ShouldBe(2);
        series.Single(s => s.Labels["channel"] == "app").Value.ShouldBe(4);
    }

    [Fact]
    public async Task Read_treats_the_same_tags_in_another_order_as_one_series()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var counter = fixture.Meter.CreateCounter<int>("orders.placed");

        // Act
        counter.Add(1, new KeyValuePair<string, object?>("a", "1"), new KeyValuePair<string, object?>("b", "2"));
        counter.Add(1, new KeyValuePair<string, object?>("b", "2"), new KeyValuePair<string, object?>("a", "1"));

        // Assert
        fixture.Series("orders.placed").Single().Value.ShouldBe(2);
    }

    [Fact]
    public async Task Read_reports_a_histogram_as_a_count_and_a_sum()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var histogram = fixture.Meter.CreateHistogram<double>("orders.duration");

        // Act
        histogram.Record(0.5);
        histogram.Record(1.5);

        // Assert
        fixture.Series("orders.duration.count").Single().Value.ShouldBe(2);
        fixture.Series("orders.duration.sum").Single().Value.ShouldBe(2);
    }

    [Fact]
    public async Task Read_asks_an_observable_gauge_for_its_value_at_the_time_of_reading()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var queued = 7;
        fixture.Meter.CreateObservableGauge("orders.queued", () => queued);

        // Act
        var first = fixture.Series("orders.queued").Single().Value;
        queued = 3;
        var second = fixture.Series("orders.queued").Single().Value;

        // Assert
        first.ShouldBe(7);
        second.ShouldBe(3);
    }

    [Fact]
    public async Task Read_keeps_only_the_latest_value_of_an_observable_counter()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        var total = 10L;
        fixture.Meter.CreateObservableCounter("orders.total", () => total);

        // Act
        fixture.Series("orders.total");
        total = 15;
        var value = fixture.Series("orders.total").Single().Value;

        // Assert: the observable reports the total itself, so it is replaced and not added up
        value.ShouldBe(15);
    }

    [Fact]
    public async Task Read_leaves_out_the_instruments_that_are_denied()
    {
        // Arrange
        using var fixture = await new CollectorFixture().Denying("secret.*", "orders.skipped").StartAsync();
        fixture.Meter.CreateCounter<int>("secret.logins").Add(1);
        fixture.Meter.CreateCounter<int>("orders.skipped").Add(1);
        fixture.Meter.CreateCounter<int>("orders.kept").Add(1);

        // Act
        var names = fixture.Names();

        // Assert
        names.ShouldContain("orders.kept");
        names.ShouldNotContain("secret.logins");
        names.ShouldNotContain("orders.skipped");
    }

    [Fact]
    public async Task Read_ignores_new_series_beyond_the_limit_but_keeps_updating_the_known_ones()
    {
        // Arrange
        using var fixture = await new CollectorFixture().WithLimit(2).StartAsync();
        var counter = fixture.Meter.CreateCounter<int>("orders.placed");
        counter.Add(1, new KeyValuePair<string, object?>("id", "1"));
        counter.Add(1, new KeyValuePair<string, object?>("id", "2"));

        // Act
        counter.Add(1, new KeyValuePair<string, object?>("id", "3"));
        counter.Add(1, new KeyValuePair<string, object?>("id", "1"));

        // Assert
        var series = fixture.Series("orders.placed");
        series.Select(s => s.Labels["id"]).ShouldBe(["1", "2"]);
        series.Single(s => s.Labels["id"] == "1").Value.ShouldBe(2);
    }

    [Fact]
    public async Task Read_includes_the_meters_of_the_runtime()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        var names = fixture.Names();

        // Assert
        names.ShouldContain(name => name.StartsWith("dotnet.gc.", StringComparison.Ordinal));
        names.ShouldContain("dotnet.process.memory.working_set");
    }

    [Fact]
    public async Task Read_finds_instruments_that_existed_before_it_started()
    {
        // Arrange
        using var meter = new Meter($"test-{Guid.NewGuid():N}");
        var counter = meter.CreateCounter<int>("orders.early");
        var collector = new MetricsCollector(new MonitoringOptions { Enabled = true });

        // Act
        await collector.StartAsync(Ct);
        counter.Add(4);

        // Assert
        collector.Read().Series.Single(s => s.Name == "orders.early").Value.ShouldBe(4);
        collector.Dispose();
    }

    [Fact]
    public async Task Read_reports_nothing_when_monitoring_is_turned_off()
    {
        // Arrange
        using var meter = new Meter($"test-{Guid.NewGuid():N}");
        var collector = new MetricsCollector(new MonitoringOptions { Enabled = false });
        await collector.StartAsync(Ct);

        // Act
        meter.CreateCounter<int>("orders.off").Add(1);

        // Assert
        collector.Read().Series.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_pull_source_answers_with_the_metrics_as_json()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        fixture.Meter.CreateCounter<int>("orders.placed").Add(1);
        var source = new MetricsPullSource(fixture.Collector);

        // Act
        var json = (await source.ReadAsync(0, Ct)).Payload;

        // Assert
        ReportPayload.ReadMetrics(json)!.Series.ShouldContain(s => s.Name == "orders.placed" && s.Value == 1);
    }

    private sealed class CollectorFixture : IDisposable
    {
        private readonly MonitoringOptions _options = new() { Enabled = true };
        private bool _ownMeterOnly;

        public Meter Meter { get; } = new($"test-{Guid.NewGuid():N}");

        public MetricsCollector Collector { get; private set; } = null!;

        public CollectorFixture Denying(params string[] patterns)
        {
            _options.MetricsDeny = [.. patterns];
            return this;
        }

        public CollectorFixture WithLimit(int series)
        {
            _options.MaxMetricSeries = series;

            // Only what this test makes counts towards the limit, whatever other tests of the process measure at the same time.
            _ownMeterOnly = true;
            return this;
        }

        public async Task<CollectorFixture> StartAsync()
        {
            Collector = new MetricsCollector(_options, _ownMeterOnly ? instrument => instrument.Meter == Meter : null);
            await Collector.StartAsync(Ct);
            return this;
        }

        /// <summary>The series of one instrument, so other tests' meters in the same process do not matter.</summary>
        public IReadOnlyList<MetricSeries> Series(string name) => [.. Collector.Read().Series.Where(s => s.Name == name)];

        public IReadOnlyList<string> Names() => [.. Collector.Read().Series.Select(s => s.Name)];

        public void Dispose()
        {
            Collector.Dispose();
            Meter.Dispose();
        }
    }
}
