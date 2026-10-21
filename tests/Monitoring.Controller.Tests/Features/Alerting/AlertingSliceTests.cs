using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Packages;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class AlertingSliceTests
{
    [Fact]
    public void AddAlerting_shares_one_evaluator_between_the_driving_and_reading_contracts()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddPackages().AddVulnerabilities().AddAlerting().BuildServiceProvider();

        // Act
        var evaluator = provider.GetRequiredService<IAlertEvaluator>();
        var state = provider.GetRequiredService<IAlertState>();

        // Assert
        state.ShouldBeSameAs(evaluator);
    }

    [Fact]
    public void AddAlerting_registers_the_default_rules()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddPackages().AddVulnerabilities().AddAlerting().BuildServiceProvider();

        // Act
        var rules = provider.GetServices<IAlertRule>().Select(r => r.Name);

        // Assert
        rules.ShouldBe(["instance-unhealthy", "service-down", "scope-unknown", "restart-loop", "package-drift", "vulnerable-package", "vulnerable-package-critical"], ignoreOrder: true);
    }

    [Fact]
    public void AddAlerting_rejects_a_silence_without_a_rule()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAlerting(o => o.Silences.Add(new AlertSilence("", "shop/", DateTimeOffset.UtcNow.AddDays(1))));

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddAlerting_rejects_a_restart_count_below_one(int count)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAlerting(o => o.RestartLoopCount = count);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddAlerting_rejects_a_restart_window_that_is_not_positive()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAlerting(o => o.RestartLoopWindow = TimeSpan.Zero);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(nameof(AlertingOptions.StartupGrace))]
    [InlineData(nameof(AlertingOptions.InstanceUnhealthyWait))]
    [InlineData(nameof(AlertingOptions.ServiceDownWait))]
    [InlineData(nameof(AlertingOptions.ScopeUnknownWait))]
    public void AddAlerting_rejects_a_negative_duration(string option)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAlerting(o =>
        {
            var negative = TimeSpan.FromSeconds(-1);
            switch (option)
            {
                case nameof(AlertingOptions.StartupGrace): o.StartupGrace = negative; break;
                case nameof(AlertingOptions.InstanceUnhealthyWait): o.InstanceUnhealthyWait = negative; break;
                case nameof(AlertingOptions.ServiceDownWait): o.ServiceDownWait = negative; break;
                default: o.ScopeUnknownWait = negative; break;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddAlerting_rejects_an_evaluation_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAlerting(o => o.EvaluationInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddAlerting_registers_the_evaluation_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddPackages().AddVulnerabilities().AddAlerting().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "AlertEvaluationService");
    }
}
