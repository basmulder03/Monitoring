using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class ScopeUnknownRuleTests
{
    [Fact]
    public void FindFailingSubjects_names_a_scope_whose_agent_is_gone()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy);
        var rule = new ScopeUnknownRule(registry, new FakeAgentLiveness().WithDeadScope("shop"), new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop"]);
    }

    [Fact]
    public void FindFailingSubjects_ignores_a_scope_whose_agent_is_alive()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy);
        var rule = new ScopeUnknownRule(registry, new FakeAgentLiveness(), new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_names_a_gone_agent_even_for_a_scope_without_instances()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithEmptyScope("shop");
        var rule = new ScopeUnknownRule(registry, new FakeAgentLiveness().WithDeadScope("shop"), new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop"]);
    }

    [Fact]
    public void FindFailingSubjects_judges_each_scope_on_its_own()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithEmptyScope("shop").WithEmptyScope("billing");
        var rule = new ScopeUnknownRule(registry, new FakeAgentLiveness().WithDeadScope("billing"), new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["billing"]);
    }

    [Fact]
    public void The_rule_is_critical_and_waits_two_minutes_by_default()
    {
        // Arrange
        var rule = new ScopeUnknownRule(new FakeScopeRegistry(), new FakeAgentLiveness(), new AlertingOptions());

        // Act
        var severity = rule.Severity;
        var wait = rule.Wait;

        // Assert
        severity.ShouldBe(AlertSeverity.Critical);
        wait.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Wait_comes_from_the_options()
    {
        // Arrange
        var options = new AlertingOptions { ScopeUnknownWait = TimeSpan.FromSeconds(30) };

        // Act
        var rule = new ScopeUnknownRule(new FakeScopeRegistry(), new FakeAgentLiveness(), options);

        // Assert
        rule.Wait.ShouldBe(TimeSpan.FromSeconds(30));
    }
}
