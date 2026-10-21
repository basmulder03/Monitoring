using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;
using static Monitoring.Protocol.Health.HealthState;
using static Monitoring.Protocol.Instances.InstanceLifecycle;

namespace Monitoring.Controller.Tests.Features.Dashboard;

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

        public ModelFixture WithInstance(string scope, string service, InstanceLifecycle lifecycle, HealthState health, string? id = null)
        {
            _registry.WithInstance(scope, service, lifecycle, health, id);
            return this;
        }

        public ModelFixture WithEmptyScope(string scope)
        {
            _registry.WithEmptyScope(scope);
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

        public DashboardModel Build() => new(_registry, new HealthRollup(_registry), new FixedAlerts(_alerts), _options);
    }

    /// <summary>Alert state that returns the alerts it was given.</summary>
    private sealed class FixedAlerts(IReadOnlyList<Alert> alerts) : IAlertState
    {
        public IReadOnlyList<Alert> GetActiveAlerts() => alerts;
    }
}
