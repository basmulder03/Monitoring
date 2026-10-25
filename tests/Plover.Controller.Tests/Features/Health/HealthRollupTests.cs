using Plover.Controller.Features.Health;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Controller.Tests.Shared;
using Shouldly;
using Xunit;
using static Plover.Protocol.Health.HealthState;
using static Plover.Protocol.Instances.InstanceLifecycle;

namespace Plover.Controller.Tests.Features.Health;

public class HealthRollupTests
{
    public static TheoryData<HealthState[], HealthState> ServiceRollups() => new()
    {
        { [Healthy, Healthy], Healthy },
        { [Healthy, Degraded], Degraded },
        { [Degraded, Unhealthy, Healthy], Unhealthy },
        { [Healthy, Unknown], Unknown },
        { [Unhealthy], Unhealthy },
    };

    [Theory]
    [MemberData(nameof(ServiceRollups))]
    public void GetServiceHealth_is_the_worst_health_of_the_live_instances(HealthState[] instanceHealths, HealthState expected)
    {
        // Arrange
        var fixture = new RollupFixture().WithInstances("shop", "orders", Running, instanceHealths);

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Single().Health.ShouldBe(expected);
    }

    [Theory]
    [InlineData(Terminating)]
    [InlineData(Gone)]
    public void GetServiceHealth_ignores_instances_that_are_not_live(InstanceLifecycle lifecycle)
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "orders", Running, Healthy)
            .WithInstances("shop", "orders", lifecycle, Unhealthy);

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Single().ShouldBe(new ServiceHealth("orders", Healthy, 1, 1));
    }

    [Fact]
    public void GetServiceHealth_counts_starting_instances_as_live()
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "orders", Starting, Degraded)
            .WithInstances("shop", "orders", Running, Healthy);

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Single().ShouldBe(new ServiceHealth("orders", Degraded, 2, 1));
    }

    [Theory]
    [InlineData(Gone)]
    [InlineData(Terminating)]
    public void GetServiceHealth_marks_a_service_without_live_instances_as_down(InstanceLifecycle lifecycle)
    {
        // Arrange
        var fixture = new RollupFixture().WithInstances("shop", "orders", lifecycle, Healthy);

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Single().ShouldBe(new ServiceHealth("orders", Unhealthy, 0, 0));
    }

    [Fact]
    public void GetServiceHealth_lists_each_service_once_ordered_by_name()
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "payments", Running, Healthy)
            .WithInstances("shop", "orders", Running, Healthy, Healthy);

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Select(s => s.ServiceName).ShouldBe(["orders", "payments"]);
    }

    [Fact]
    public void GetServiceHealth_is_empty_for_an_unknown_scope()
    {
        // Arrange
        var fixture = new RollupFixture();

        // Act
        var services = fixture.Rollup.GetServiceHealth("nowhere");

        // Assert
        services.ShouldBeEmpty();
    }

    [Fact]
    public void GetScopeHealth_is_the_worst_health_of_its_services()
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "orders", Running, Healthy)
            .WithInstances("shop", "payments", Running, Degraded);

        // Act
        var health = fixture.Rollup.GetScopeHealth("shop");

        // Assert
        health.ShouldBe(Degraded);
    }

    [Fact]
    public void GetScopeHealth_turns_unhealthy_when_one_service_is_down()
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "orders", Running, Healthy)
            .WithInstances("shop", "payments", Gone, Healthy);

        // Act
        var health = fixture.Rollup.GetScopeHealth("shop");

        // Assert
        health.ShouldBe(Unhealthy);
    }

    [Fact]
    public void GetScopeHealth_is_unknown_when_nothing_is_known_about_the_scope()
    {
        // Arrange
        var fixture = new RollupFixture();

        // Act
        var health = fixture.Rollup.GetScopeHealth("nowhere");

        // Assert
        health.ShouldBe(Unknown);
    }

    [Fact]
    public void GetScopeHealth_is_unknown_while_the_agent_of_the_scope_is_gone_whatever_was_reported_last()
    {
        // Arrange
        var fixture = new RollupFixture().WithInstances("shop", "orders", Running, Healthy).WithAgentGone("shop");

        // Act
        var health = fixture.Rollup.GetScopeHealth("shop");

        // Assert
        health.ShouldBe(Unknown);
    }

    [Fact]
    public void GetScopeHealth_is_unknown_for_a_gone_agent_even_when_the_last_report_was_bad()
    {
        // Arrange
        var fixture = new RollupFixture().WithInstances("shop", "orders", Running, Unhealthy).WithAgentGone("shop");

        // Act
        var health = fixture.Rollup.GetScopeHealth("shop");

        // Assert
        health.ShouldBe(Unknown);
    }

    [Fact]
    public void GetScopeHealth_only_affects_the_scope_whose_agent_is_gone()
    {
        // Arrange
        var fixture = new RollupFixture()
            .WithInstances("shop", "orders", Running, Healthy)
            .WithInstances("billing", "invoices", Running, Healthy)
            .WithAgentGone("shop");

        // Act
        var health = fixture.Rollup.GetScopeHealth("billing");

        // Assert
        health.ShouldBe(Healthy);
    }

    [Fact]
    public void GetServiceHealth_still_reports_the_last_known_state_when_the_agent_is_gone()
    {
        // Arrange
        var fixture = new RollupFixture().WithInstances("shop", "orders", Running, Degraded).WithAgentGone("shop");

        // Act
        var services = fixture.Rollup.GetServiceHealth("shop");

        // Assert
        services.Single().Health.ShouldBe(Degraded);
    }

    /// <summary>Builds a roll-up over a registry that holds exactly the instances the test adds.</summary>
    private sealed class RollupFixture
    {
        private readonly FakeScopeRegistry _registry = new();

        public RollupFixture()
        {
            Rollup = new HealthRollup(_registry, Liveness);
        }

        public IHealthRollup Rollup { get; }

        public FakeAgentLiveness Liveness { get; } = new();

        /// <summary>Makes the agent of the scope count as gone.</summary>
        public RollupFixture WithAgentGone(string scope)
        {
            Liveness.WithDeadScope(scope);
            return this;
        }

        /// <summary>Adds one instance per given health, all in the same scope, service and lifecycle.</summary>
        public RollupFixture WithInstances(string scope, string service, InstanceLifecycle lifecycle, params HealthState[] healths)
        {
            foreach (var health in healths)
            {
                _registry.WithInstance(scope, service, lifecycle, health);
            }

            return this;
        }
    }
}
