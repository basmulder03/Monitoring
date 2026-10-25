using Plover.Protocol.Health;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Protocol.Tests.Reports;

public class ReportPayloadTests
{
    [Fact]
    public void ReadDescription_restores_what_was_written()
    {
        // Arrange
        var json = ReportPayload.Write(new InstanceDescription("orders", "1.4.2"));

        // Act
        var description = ReportPayload.ReadDescription(json);

        // Assert
        description!.ServiceName.ShouldBe("orders");
        description.Version.ShouldBe("1.4.2");
    }

    [Fact]
    public void ReadDescription_accepts_an_application_that_does_not_know_its_version()
    {
        // Arrange
        var json = ReportPayload.Write(new InstanceDescription("orders"));

        // Act
        var description = ReportPayload.ReadDescription(json);

        // Assert
        description!.Version.ShouldBeNull();
        json.ShouldNotContain("version");
    }

    [Fact]
    public void ReadMetrics_restores_every_series_with_its_labels()
    {
        // Arrange
        var report = new MetricsReport([
            new MetricSeries("orders.placed", new Dictionary<string, string> { ["channel"] = "web" }, 12.5),
            new MetricSeries("orders.queued", new Dictionary<string, string>(), 3),
        ]);

        // Act
        var read = ReportPayload.ReadMetrics(ReportPayload.Write(report));

        // Assert
        read!.Series.Count.ShouldBe(2);
        read.Series[0].Name.ShouldBe("orders.placed");
        read.Series[0].Labels["channel"].ShouldBe("web");
        read.Series[0].Value.ShouldBe(12.5);
        read.Series[1].Labels.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void ReadMetrics_returns_nothing_for_a_payload_that_is_not_a_report(string? json)
    {
        // Arrange (nothing to prepare)

        // Act
        var read = ReportPayload.ReadMetrics(json);

        // Assert
        read.ShouldBeNull();
    }

    [Fact]
    public void ReadHealth_restores_the_result_of_every_check()
    {
        // Arrange
        var report = new HealthReport(HealthState.Unhealthy, [
            new HealthCheckResult("database", HealthState.Unhealthy, "connection refused"),
            new HealthCheckResult("cache", HealthState.Healthy),
        ]);

        // Act
        var read = ReportPayload.ReadHealth(ReportPayload.Write(report));

        // Assert
        read!.Health.ShouldBe(HealthState.Unhealthy);
        read.Checks.Select(c => c.Name).ShouldBe(["database", "cache"]);
        read.Checks[0].Health.ShouldBe(HealthState.Unhealthy);
        read.Checks[0].Description.ShouldBe("connection refused");
        read.Checks[1].Description.ShouldBeNull();
    }

    [Fact]
    public void Write_gives_health_as_text_so_other_implementations_can_read_it()
    {
        // Arrange
        var report = new HealthReport(HealthState.Degraded, []);

        // Act
        var json = ReportPayload.Write(report);

        // Assert
        json.ShouldContain("\"health\":\"Degraded\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"health\":\"Sideways\",\"checks\":[]}")]
    public void ReadHealth_treats_a_bad_report_as_none(string? json)
    {
        // Arrange: applications are not trusted to send well-formed reports

        // Act
        var report = ReportPayload.ReadHealth(json);

        // Assert
        report.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    public void ReadDescription_treats_a_bad_report_as_none(string? json)
    {
        // Arrange: applications are not trusted to send well-formed reports

        // Act
        var description = ReportPayload.ReadDescription(json);

        // Assert
        description.ShouldBeNull();
    }

    [Fact]
    public void ReadHealth_ignores_fields_from_a_newer_application()
    {
        // Arrange
        const string json = """{"health":"Healthy","checks":[],"somethingNew":42}""";

        // Act
        var report = ReportPayload.ReadHealth(json);

        // Assert
        report!.Health.ShouldBe(HealthState.Healthy);
    }
}
