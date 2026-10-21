using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class InstanceUnhealthyRuleTests
{
    [Fact]
    public void FindFailingSubjects_names_a_running_unhealthy_instance_by_scope_service_and_id()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Unhealthy, "pod-1");
        var rule = new InstanceUnhealthyRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders/pod-1"]);
    }

    [Theory]
    [InlineData(InstanceLifecycle.Running, HealthState.Healthy)]
    [InlineData(InstanceLifecycle.Running, HealthState.Degraded)]
    [InlineData(InstanceLifecycle.Running, HealthState.Unknown)]
    [InlineData(InstanceLifecycle.Starting, HealthState.Unhealthy)]
    [InlineData(InstanceLifecycle.Terminating, HealthState.Unhealthy)]
    [InlineData(InstanceLifecycle.Gone, HealthState.Unhealthy)]
    public void FindFailingSubjects_ignores_instances_that_are_not_running_and_unhealthy(InstanceLifecycle lifecycle, HealthState health)
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", lifecycle, health);
        var rule = new InstanceUnhealthyRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_covers_every_scope()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Unhealthy, "a")
            .WithInstance("billing", "invoices", InstanceLifecycle.Running, HealthState.Unhealthy, "b");
        var rule = new InstanceUnhealthyRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["billing/invoices/b", "shop/orders/a"], ignoreOrder: true);
    }

    [Fact]
    public void Wait_comes_from_the_options()
    {
        // Arrange
        var options = new AlertingOptions { InstanceUnhealthyWait = TimeSpan.FromSeconds(45) };

        // Act
        var rule = new InstanceUnhealthyRule(new FakeScopeRegistry(), options);

        // Assert
        rule.Wait.ShouldBe(TimeSpan.FromSeconds(45));
    }
}
