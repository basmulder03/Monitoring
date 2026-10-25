using Plover.Controller.Features.Alerting;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Shouldly;
using Xunit;
using static Plover.Protocol.Health.HealthState;
using static Plover.Protocol.Instances.InstanceLifecycle;

namespace Plover.Controller.Tests.Features.Alerting;

public class ServiceDownRuleTests
{
    [Theory]
    [InlineData(Running, Healthy)]
    [InlineData(Running, Degraded)]
    [InlineData(Starting, Healthy)]
    public void FindFailingSubjects_ignores_a_service_with_an_instance_that_can_serve(InstanceLifecycle lifecycle, HealthState health)
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", lifecycle, health);
        var rule = new ServiceDownRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Running, Unhealthy)]
    [InlineData(Running, Unknown)]
    [InlineData(Starting, Unhealthy)]
    [InlineData(Terminating, Healthy)]
    [InlineData(Gone, Healthy)]
    public void FindFailingSubjects_names_a_service_without_an_instance_that_can_serve(InstanceLifecycle lifecycle, HealthState health)
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", lifecycle, health);
        var rule = new ServiceDownRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders"]);
    }

    [Fact]
    public void FindFailingSubjects_ignores_a_service_with_one_failing_and_one_healthy_instance()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", "orders", Running, Unhealthy)
            .WithInstance("shop", "orders", Running, Healthy);
        var rule = new ServiceDownRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_judges_each_service_of_each_scope_on_its_own()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", "orders", Running, Healthy)
            .WithInstance("shop", "payments", Running, Unhealthy)
            .WithInstance("billing", "payments", Running, Healthy);
        var rule = new ServiceDownRule(registry, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/payments"]);
    }

    [Fact]
    public void Severity_is_critical_and_wait_comes_from_the_options()
    {
        // Arrange
        var options = new AlertingOptions { ServiceDownWait = TimeSpan.FromMinutes(1) };

        // Act
        var rule = new ServiceDownRule(new FakeScopeRegistry(), options);

        // Assert
        rule.Severity.ShouldBe(AlertSeverity.Critical);
        rule.Wait.ShouldBe(TimeSpan.FromMinutes(1));
    }
}
