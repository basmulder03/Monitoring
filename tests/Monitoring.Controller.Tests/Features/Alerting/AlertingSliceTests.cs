using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class AlertingSliceTests
{
    [Fact]
    public void AddAlerting_shares_one_evaluator_between_the_driving_and_reading_contracts()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddAlerting().BuildServiceProvider();

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
        using var provider = new ServiceCollection().AddRegistry().AddAlerting().BuildServiceProvider();

        // Act
        var rules = provider.GetServices<IAlertRule>().Select(r => r.Name);

        // Assert
        rules.ShouldBe(["instance-unhealthy", "service-down"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(nameof(AlertingOptions.StartupGrace))]
    [InlineData(nameof(AlertingOptions.InstanceUnhealthyWait))]
    [InlineData(nameof(AlertingOptions.ServiceDownWait))]
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
                default: o.ServiceDownWait = negative; break;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
