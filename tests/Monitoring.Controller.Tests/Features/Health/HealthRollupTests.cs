using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;
using static Monitoring.Protocol.Health.HealthState;
using static Monitoring.Protocol.Instances.InstanceLifecycle;

namespace Monitoring.Controller.Tests.Features.Health;

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

    /// <summary>Builds a roll-up over a registry that holds exactly the instances the test adds.</summary>
    private sealed class RollupFixture
    {
        private readonly FakeRegistry _registry = new();

        public RollupFixture()
        {
            Rollup = new HealthRollup(_registry);
        }

        public IHealthRollup Rollup { get; }

        /// <summary>Adds one instance per given health, all in the same scope, service and lifecycle.</summary>
        public RollupFixture WithInstances(string scope, string service, InstanceLifecycle lifecycle, params HealthState[] healths)
        {
            foreach (var health in healths)
            {
                var id = $"{service}-{_registry.Count(scope)}";
                _registry.Add(scope, new InstanceInfo(id, service, lifecycle, health, new Dictionary<string, string>()));
            }

            return this;
        }
    }

    /// <summary>Registry stand-in that only answers reads; the roll-up never writes.</summary>
    private sealed class FakeRegistry : IScopeRegistry
    {
        private readonly Dictionary<string, List<InstanceInfo>> _scopes = [];

        public void Add(string scope, InstanceInfo instance)
        {
            if (!_scopes.TryGetValue(scope, out var list))
            {
                _scopes[scope] = list = [];
            }

            list.Add(instance);
        }

        public int Count(string scope) => _scopes.TryGetValue(scope, out var list) ? list.Count : 0;

        public IReadOnlyList<InstanceInfo> GetInstances(string scopeName) =>
            _scopes.TryGetValue(scopeName, out var list) ? list : [];

        public ApplyResult ApplySnapshot(Snapshot snapshot) => throw new NotSupportedException();

        public ApplyResult ApplyDelta(string scopeName, Delta delta) => throw new NotSupportedException();

        public void PurgeExpired() => throw new NotSupportedException();
    }
}
