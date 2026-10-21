using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Monitoring.Client.Features.Health;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Health;
using Shouldly;
using Xunit;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Monitoring.Client.Tests.Features.Health;

public class HealthReporterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReportAsync_is_healthy_for_an_application_without_health_checks()
    {
        // Arrange
        var reporter = new ReporterFixture().Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Healthy);
        report.Checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReportAsync_lists_every_check_ordered_by_name()
    {
        // Arrange
        var reporter = new ReporterFixture()
            .WithCheck("queue", HealthStatus.Healthy)
            .WithCheck("database", HealthStatus.Healthy)
            .Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Checks.Select(c => c.Name).ShouldBe(["database", "queue"]);
        report.Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public async Task ReportAsync_shows_a_failing_check_as_unhealthy()
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("database", HealthStatus.Unhealthy).Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Unhealthy);
        report.Checks.Single().Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task ReportAsync_shows_a_failing_non_critical_check_as_degraded()
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("recommendations", HealthStatus.Unhealthy, nonCritical: true).Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Degraded);
        report.Checks.Single().Health.ShouldBe(HealthState.Degraded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportAsync_keeps_a_degraded_check_degraded_whether_it_is_critical_or_not(bool nonCritical)
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("cache", HealthStatus.Degraded, nonCritical).Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Degraded);
    }

    [Fact]
    public async Task ReportAsync_takes_the_worst_result_of_all_checks()
    {
        // Arrange
        var reporter = new ReporterFixture()
            .WithCheck("a", HealthStatus.Healthy)
            .WithCheck("b", HealthStatus.Degraded)
            .WithCheck("c", HealthStatus.Unhealthy, nonCritical: true)
            .WithCheck("d", HealthStatus.Unhealthy)
            .Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task ReportAsync_carries_the_description_of_a_check()
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("database", HealthStatus.Unhealthy, description: "connection refused").Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Checks.Single().Description.ShouldBe("connection refused");
    }

    [Fact]
    public async Task ReportAsync_uses_the_message_of_the_exception_of_a_check_that_threw()
    {
        // Arrange
        var reporter = new ReporterFixture().WithThrowingCheck("database", "boom").Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Unhealthy);
        report.Checks.Single().Description.ShouldBe("boom");
    }

    [Fact]
    public async Task ReportAsync_shortens_a_very_long_description()
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("database", HealthStatus.Unhealthy, description: new string('x', 2000)).Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Checks.Single().Description!.Length.ShouldBeLessThan(600);
        report.Checks.Single().Description!.ShouldEndWith("...");
    }

    [Fact]
    public async Task ReportAsync_leaves_out_the_description_when_there_is_nothing_to_say()
    {
        // Arrange
        var reporter = new ReporterFixture().WithCheck("database", HealthStatus.Healthy).Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Checks.Single().Description.ShouldBeNull();
    }

    [Fact]
    public async Task ReportAsync_reports_checks_that_do_not_finish_in_time_as_unhealthy()
    {
        // Arrange
        var reporter = new ReporterFixture().WithHealthTimeout(TimeSpan.FromMilliseconds(100)).WithHangingCheck("database").Build();

        // Act
        var report = await reporter.ReportAsync(Ct);

        // Assert
        report.Health.ShouldBe(HealthState.Unhealthy);
        report.Checks.Single().Description!.ShouldContain("did not finish");
    }

    [Fact]
    public async Task ReportAsync_stops_when_the_caller_cancels()
    {
        // Arrange
        var reporter = new ReporterFixture().WithHealthTimeout(TimeSpan.FromSeconds(30)).WithHangingCheck("database").Build();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var report = async () => await reporter.ReportAsync(cancelled.Token);

        // Assert
        await report.ShouldThrowAsync<OperationCanceledException>();
    }

    /// <summary>Builds a reporter over health checks the test registers.</summary>
    private sealed class ReporterFixture
    {
        private readonly ServiceCollection _services = new();
        private readonly MonitoringOptions _options = new();
        private readonly IHealthChecksBuilder _checks;

        public ReporterFixture()
        {
            _services.AddLogging();
            _checks = _services.AddHealthChecks();
        }

        public ReporterFixture WithHealthTimeout(TimeSpan timeout)
        {
            _options.HealthTimeout = timeout;
            return this;
        }

        public ReporterFixture WithCheck(string name, HealthStatus status, bool nonCritical = false, string? description = null)
        {
            _checks.Add(Registration(name, _ => Task.FromResult(new HealthCheckResult(status, description)), nonCritical));
            return this;
        }

        public ReporterFixture WithThrowingCheck(string name, string message)
        {
            _checks.Add(Registration(name, _ => throw new InvalidOperationException(message), nonCritical: false));
            return this;
        }

        public ReporterFixture WithHangingCheck(string name)
        {
            _checks.Add(Registration(name, async ct =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return HealthCheckResult.Healthy();
            }, nonCritical: false));
            return this;
        }

        public HealthReporter Build()
        {
            _services.AddSingleton(_options);
            _services.AddSingleton<HealthReporter>();
            return _services.BuildServiceProvider().GetRequiredService<HealthReporter>();
        }

        private static HealthCheckRegistration Registration(string name, Func<CancellationToken, Task<HealthCheckResult>> run, bool nonCritical) =>
            new(name, new DelegateCheck(run), failureStatus: null, tags: nonCritical ? [MonitoringTags.NonCritical] : []);
    }

    private sealed class DelegateCheck(Func<CancellationToken, Task<HealthCheckResult>> run) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            run(cancellationToken);
    }
}
