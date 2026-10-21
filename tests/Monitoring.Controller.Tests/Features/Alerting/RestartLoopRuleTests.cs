using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class RestartLoopRuleTests
{
    [Fact]
    public void FindFailingSubjects_names_an_instance_that_restarted_three_times_within_ten_minutes()
    {
        // Arrange
        var fixture = new RuleFixture().Restarts(0);
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));

        // Act
        fixture.Restarts(3);
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders/pod-1"]);
    }

    [Fact]
    public void FindFailingSubjects_ignores_fewer_restarts_than_the_count()
    {
        // Arrange
        var fixture = new RuleFixture().Restarts(0);
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));

        // Act
        fixture.Restarts(2);
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_does_not_count_restarts_from_before_it_started_watching()
    {
        // Arrange
        var fixture = new RuleFixture().Restarts(40);

        // Act
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert: an old instance with a long history is not a loop
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_stops_naming_an_instance_once_the_restarts_are_older_than_the_window()
    {
        // Arrange
        var fixture = new RuleFixture().Restarts(0);
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Restarts(3);
        fixture.Rule.FindFailingSubjects().ToList().ShouldNotBeEmpty();

        // Act: it has been quiet for longer than the window
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_adds_up_the_restarts_of_the_containers_but_not_of_init_containers()
    {
        // Arrange
        var fixture = new RuleFixture().Containers(
            Container("orders", ContainerRole.App, 0), Container("proxy", ContainerRole.OtherSidecar, 0), Container("migrate", ContainerRole.Init, 0));
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        fixture.Containers(Container("orders", ContainerRole.App, 2), Container("proxy", ContainerRole.OtherSidecar, 1), Container("migrate", ContainerRole.Init, 9));
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders/pod-1"]);
    }

    [Fact]
    public void FindFailingSubjects_ignores_instances_that_report_no_containers()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy, "pod-1");
        var rule = new RestartLoopRule(registry, new AlertingOptions(), new FakeTimeProvider());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void FindFailingSubjects_starts_over_for_an_instance_that_was_replaced()
    {
        // Arrange
        var fixture = new RuleFixture().Restarts(5);
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Gone();
        fixture.Rule.FindFailingSubjects().ToList();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: a new pod with the same name, counting from zero
        fixture.Restarts(0);
        var subjects = fixture.Rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    private static ContainerInfo Container(string name, ContainerRole role, int restarts) =>
        new(name, role, ContainerPhase.Running, HealthState.Healthy, true, restarts);

    private sealed class RuleFixture
    {
        private readonly FakeScopeRegistry _registry = new();

        public RuleFixture()
        {
            Rule = new RestartLoopRule(_registry, new AlertingOptions(), Clock);
        }

        public FakeTimeProvider Clock { get; } = new();

        public RestartLoopRule Rule { get; }

        public RuleFixture Restarts(int restarts) => Containers(Container("orders", ContainerRole.App, restarts));

        public RuleFixture Containers(params ContainerInfo[] containers)
        {
            _registry.WithEmptyScope("shop").WithInstance(
                "shop",
                new InstanceInfo("pod-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), Containers: containers));
            return this;
        }

        public void Gone() => _registry.WithEmptyScope("shop");
    }
}
