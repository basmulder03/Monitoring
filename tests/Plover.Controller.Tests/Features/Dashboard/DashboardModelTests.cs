using Plover.Controller.Features.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Dashboard;
using Plover.Controller.Features.Health;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Shouldly;
using Xunit;
using static Plover.Protocol.Health.HealthState;
using static Plover.Protocol.Instances.InstanceLifecycle;

namespace Plover.Controller.Tests.Features.Dashboard;

public class DashboardModelTests
{
    [Fact]
    public void BuildOverview_is_empty_when_nothing_is_known()
    {
        // Arrange
        var model = new ModelFixture().Build();

        // Act
        var overview = model.BuildOverview();

        // Assert
        overview.Scopes.ShouldBeEmpty();
        overview.Alerts.ShouldBeEmpty();
    }

    [Fact]
    public void BuildOverview_summarizes_each_scope()
    {
        // Arrange
        var model = new ModelFixture()
            .WithInstance("shop", "orders", Running, Healthy)
            .WithInstance("shop", "orders", Running, Degraded)
            .WithInstance("shop", "payments", Running, Healthy)
            .Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert
        scope.Name.ShouldBe("shop");
        scope.Health.ShouldBe(Degraded);
        scope.Services.ShouldBe(2);
        scope.Instances.ShouldBe(3);
        scope.Problems.ShouldBe(1);
    }

    [Fact]
    public void BuildOverview_counts_only_live_instances()
    {
        // Arrange
        var model = new ModelFixture()
            .WithInstance("shop", "orders", Running, Healthy)
            .WithInstance("shop", "orders", Starting, Unknown)
            .WithInstance("shop", "orders", Terminating, Unhealthy)
            .WithInstance("shop", "orders", Gone, Unhealthy)
            .Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert
        scope.Instances.ShouldBe(2);
        scope.Problems.ShouldBe(1);
    }

    [Fact]
    public void BuildOverview_lists_the_unhealthiest_scopes_first_then_by_name()
    {
        // Arrange
        var model = new ModelFixture()
            .WithInstance("alpha", "svc", Running, Healthy)
            .WithInstance("zulu", "svc", Running, Unhealthy)
            .WithInstance("bravo", "svc", Running, Degraded)
            .WithInstance("charlie", "svc", Running, Healthy)
            .Build();

        // Act
        var names = model.BuildOverview().Scopes.Select(s => s.Name);

        // Assert
        names.ShouldBe(["zulu", "bravo", "alpha", "charlie"]);
    }

    [Fact]
    public void BuildOverview_includes_the_alerts_that_are_firing()
    {
        // Arrange
        var since = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
        var model = new ModelFixture().WithAlert(new Alert("service-down:shop/orders", "service-down", "shop/orders", AlertSeverity.Critical, since)).Build();

        // Act
        var alert = model.BuildOverview().Alerts.Single();

        // Assert
        alert.Key.ShouldBe("service-down:shop/orders");
        alert.Rule.ShouldBe("service-down");
        alert.Subject.ShouldBe("shop/orders");
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.Since.ShouldBe(since);
    }

    [Fact]
    public void BuildScope_lists_the_services_unhealthiest_first_with_all_their_instances()
    {
        // Arrange
        var model = new ModelFixture()
            .WithInstance("shop", "orders", Running, Healthy, "orders-2")
            .WithInstance("shop", "orders", Running, Healthy, "orders-1")
            .WithInstance("shop", "orders", Gone, Healthy, "orders-0")
            .WithInstance("shop", "payments", Running, Unhealthy, "payments-1")
            .Build();

        // Act
        var scope = model.BuildScope("shop")!;

        // Assert
        scope.Name.ShouldBe("shop");
        scope.Health.ShouldBe(Unhealthy);
        scope.Services.Select(s => s.Name).ShouldBe(["payments", "orders"]);
        var orders = scope.Services[1];
        orders.LiveInstances.ShouldBe(2);
        orders.HealthyInstances.ShouldBe(2);
        orders.Instances.Select(i => i.Id).ShouldBe(["orders-0", "orders-1", "orders-2"]);
        orders.Instances.Single(i => i.Id == "orders-0").Lifecycle.ShouldBe(Gone);
    }

    [Fact]
    public void BuildOverview_carries_the_labels_of_each_scope()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).WithLabel("shop", "environment", "prod").Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert
        scope.Labels["environment"].ShouldBe("prod");
        scope.Labels.Count.ShouldBe(1);
    }

    [Fact]
    public void BuildOverview_tells_the_page_which_labels_to_group_by()
    {
        // Arrange
        var model = new ModelFixture().GroupingBy("environment", "site").Build();

        // Act
        var groupBy = model.BuildOverview().GroupBy;

        // Assert
        groupBy.ShouldBe(["environment", "site"]);
    }

    [Fact]
    public void BuildOverview_groups_by_environment_and_datacenter_unless_told_otherwise()
    {
        // Arrange
        var model = new ModelFixture().Build();

        // Act
        var groupBy = model.BuildOverview().GroupBy;

        // Assert
        groupBy.ShouldBe(["environment", "datacenter"]);
    }

    [Fact]
    public void BuildScope_carries_what_an_instance_says_about_itself()
    {
        // Arrange
        var checks = new[] { new HealthCheckResult("database", Degraded, "slow") };
        var containers = new[] { new ContainerInfo("orders", ContainerRole.App, ContainerPhase.Running, Healthy, true, 2) };
        var metrics = new[] { new Plover.Protocol.Reports.MetricSeries("orders.placed", new Dictionary<string, string>(), 3) };
        var model = new ModelFixture()
            .WithInstance("shop", new InstanceInfo("orders-1", "orders", Running, Degraded, new Dictionary<string, string>(), "1.2.3", checks, metrics, containers))
            .Build();

        // Act
        var instance = model.BuildScope("shop")!.Services.Single().Instances.Single();

        // Assert
        instance.Version.ShouldBe("1.2.3");
        instance.Checks.ShouldBe(checks);
        instance.Containers.ShouldBe(containers);
        instance.Metrics.ShouldBe(metrics);
    }

    [Fact]
    public void BuildScope_gives_an_instance_that_reports_nothing_empty_lists()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).Build();

        // Act
        var instance = model.BuildScope("shop")!.Services.Single().Instances.Single();

        // Assert
        instance.Version.ShouldBeNull();
        instance.Checks.ShouldBeEmpty();
        instance.Containers.ShouldBeEmpty();
        instance.Metrics.ShouldBeEmpty();
    }

    [Fact]
    public void BuildScope_carries_the_labels_of_the_scope()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).WithLabel("shop", "datacenter", "a").Build();

        // Act
        var scope = model.BuildScope("shop")!;

        // Assert
        scope.Labels["datacenter"].ShouldBe("a");
    }

    [Fact]
    public void BuildOverview_marks_a_scope_with_a_live_agent_as_connected_without_a_last_seen_time()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert: a time that keeps changing would make every heartbeat look like a change
        scope.AgentConnected.ShouldBeTrue();
        scope.LastSeen.ShouldBeNull();
    }

    [Fact]
    public void BuildOverview_shows_a_scope_whose_agent_is_gone_as_unknown_with_the_time_it_was_last_heard_from()
    {
        // Arrange
        var lastSeen = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).WithAgentGone("shop", lastSeen).Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert
        scope.AgentConnected.ShouldBeFalse();
        scope.Health.ShouldBe(Unknown);
        scope.LastSeen.ShouldBe(lastSeen);
    }

    [Fact]
    public void BuildOverview_keeps_the_last_known_counts_of_a_scope_whose_agent_is_gone()
    {
        // Arrange
        var model = new ModelFixture()
            .WithInstance("shop", "orders", Running, Healthy)
            .WithInstance("shop", "orders", Running, Degraded)
            .WithAgentGone("shop")
            .Build();

        // Act
        var scope = model.BuildOverview().Scopes.Single();

        // Assert
        scope.Instances.ShouldBe(2);
        scope.Problems.ShouldBe(1);
    }

    [Fact]
    public void BuildScope_says_whether_the_agent_is_connected_and_when_it_was_last_heard_from()
    {
        // Arrange
        var lastSeen = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).WithAgentGone("shop", lastSeen).Build();

        // Act
        var scope = model.BuildScope("shop")!;

        // Assert
        scope.AgentConnected.ShouldBeFalse();
        scope.LastSeen.ShouldBe(lastSeen);
        scope.Services.Single().Health.ShouldBe(Healthy);
    }

    [Fact]
    public void BuildScope_has_no_last_seen_time_while_the_agent_is_connected()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).Build();

        // Act
        var scope = model.BuildScope("shop")!;

        // Assert
        scope.AgentConnected.ShouldBeTrue();
        scope.LastSeen.ShouldBeNull();
    }

    [Fact]
    public void BuildScope_is_null_for_a_scope_that_is_not_known()
    {
        // Arrange
        var model = new ModelFixture().WithInstance("shop", "orders", Running, Healthy).Build();

        // Act
        var scope = model.BuildScope("nowhere");

        // Assert
        scope.ShouldBeNull();
    }

    [Fact]
    public void BuildScope_shows_a_known_scope_without_instances_as_empty()
    {
        // Arrange
        var model = new ModelFixture().WithEmptyScope("shop").Build();

        // Act
        var scope = model.BuildScope("shop")!;

        // Assert
        scope.Services.ShouldBeEmpty();
        scope.Health.ShouldBe(Unknown);
    }

    /// <summary>Builds the model over a fake registry, the real health roll-up and fixed alerts.</summary>
    private sealed class ModelFixture
    {
        private readonly FakeScopeRegistry _registry = new();
        private readonly List<Alert> _alerts = [];
        private readonly DashboardOptions _options = new();
        private readonly FakeAgentLiveness _liveness = new();

        public ModelFixture WithInstance(string scope, string service, InstanceLifecycle lifecycle, HealthState health, string? id = null)
        {
            _registry.WithInstance(scope, service, lifecycle, health, id);
            return this;
        }

        public ModelFixture WithInstance(string scope, InstanceInfo instance)
        {
            _registry.WithInstance(scope, instance);
            return this;
        }

        public ModelFixture WithEmptyScope(string scope)
        {
            _registry.WithEmptyScope(scope);
            return this;
        }

        public ModelFixture WithAgentGone(string scope, DateTimeOffset? lastSeen = null)
        {
            _liveness.WithDeadScope(scope, lastSeen);
            return this;
        }

        public ModelFixture WithLabel(string scope, string key, string value)
        {
            _registry.WithLabel(scope, key, value);
            return this;
        }

        public ModelFixture GroupingBy(params string[] keys)
        {
            _options.GroupBy = [.. keys];
            return this;
        }

        public ModelFixture WithAlert(Alert alert)
        {
            _alerts.Add(alert);
            return this;
        }

        public DashboardModel Build() => new(_registry, new HealthRollup(_registry, _liveness), new FixedAlerts(_alerts), _liveness, Applied, _options);

        public AppliedConfigurations Applied { get; } = new(new EffectiveConfiguration(NullLogger<EffectiveConfiguration>.Instance));
    }

    /// <summary>Alert state that returns the alerts it was given.</summary>
    private sealed class FixedAlerts(IReadOnlyList<Alert> alerts) : IAlertState
    {
        public IReadOnlyList<Alert> GetActiveAlerts() => alerts;
    }
}
