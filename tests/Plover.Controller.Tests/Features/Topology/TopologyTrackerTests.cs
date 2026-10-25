using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Topology;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Topology;

public class TopologyTrackerTests
{
    [Fact]
    public void GetGraph_is_empty_before_the_first_sample()
    {
        // Arrange
        var fixture = new TrackerFixture();

        // Act
        var graph = fixture.Tracker.GetGraph();

        // Assert
        graph.Nodes.ShouldBeEmpty();
        graph.Edges.ShouldBeEmpty();
    }

    [Fact]
    public void Sample_puts_a_queue_between_the_service_that_sends_to_it_and_the_one_that_takes_from_it()
    {
        // Arrange
        var fixture = new TrackerFixture()
            .With("shop", Instance("orders", "orders-1", Edge("MQ", "activemq://orders", 5, 0)))
            .With("shop", Instance("billing", "billing-1", Edge("MQ-IN", "activemq://orders", 4, 1)));

        // Act
        fixture.Tracker.Sample();
        var graph = fixture.Tracker.GetGraph();

        // Assert
        graph.Edges.Select(e => (e.From, e.To, e.Type, e.Calls)).ShouldBe(
        [
            ("queue:activemq://orders", "service:shop/billing", "MQ", 4L),
            ("service:shop/orders", "queue:activemq://orders", "MQ", 5L),
        ]);
        graph.Nodes.Count(n => n.Kind == NodeKind.Queue).ShouldBe(1);
    }

    [Fact]
    public void Sample_connects_a_service_to_the_service_it_calls()
    {
        // Arrange
        var fixture = new TrackerFixture()
            .With("shop", Instance("orders", "orders-1", Edge("HTTP", "payments:8080", 10, 1)))
            .With("shop", Instance("payments", "payments-1"));

        // Act
        fixture.Tracker.Sample();
        var graph = fixture.Tracker.GetGraph();

        // Assert
        graph.Edges.Count.ShouldBe(1);
        var edge = graph.Edges.Single();
        edge.From.ShouldBe("service:shop/orders");
        edge.To.ShouldBe("service:shop/payments");
        edge.Calls.ShouldBe(10);
        edge.Errors.ShouldBe(1);
        graph.Nodes.Select(n => n.Id).ShouldBe(["service:shop/orders", "service:shop/payments"]);
    }

    [Fact]
    public void Sample_adds_up_the_calls_of_all_instances_of_the_caller()
    {
        // Arrange
        var fixture = new TrackerFixture()
            .With("shop", Instance("orders", "orders-1", Edge("HTTP", "payments:8080", 10, 1)))
            .With("shop", Instance("orders", "orders-2", Edge("HTTP", "payments:8080", 5, 0)))
            .With("shop", Instance("payments", "payments-1"));

        // Act
        fixture.Tracker.Sample();

        // Assert
        var edge = fixture.Tracker.GetGraph().Edges.Single();
        edge.Calls.ShouldBe(15);
        edge.Errors.ShouldBe(1);
    }

    [Fact]
    public void Sample_gives_a_service_node_the_worst_health_of_its_instances()
    {
        // Arrange
        var fixture = new TrackerFixture()
            .With("shop", Instance("orders", "orders-1", Edge("HTTP", "payments:8080", 1, 0)))
            .With("shop", Instance("payments", "payments-1", health: HealthState.Healthy))
            .With("shop", Instance("payments", "payments-2", health: HealthState.Unhealthy));

        // Act
        fixture.Tracker.Sample();

        // Assert
        fixture.Tracker.GetGraph().Nodes.Single(n => n.Name == "payments").Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public void Sample_keeps_databases_queues_and_external_systems_as_nodes_that_are_not_services()
    {
        // Arrange
        var fixture = new TrackerFixture().With("shop", Instance(
            "orders",
            "orders-1",
            Edge("DB", "mssql://db1:1433/orders", 3, 0),
            Edge("MQ", "activemq://orders", 2, 0),
            Edge("HTTP", "api.example.org:443", 1, 0)));

        // Act
        fixture.Tracker.Sample();

        // Assert
        var nodes = fixture.Tracker.GetGraph().Nodes;
        nodes.Select(n => n.Kind).Distinct().Order().ShouldBe([NodeKind.Service, NodeKind.Database, NodeKind.Queue, NodeKind.External], ignoreOrder: true);
        nodes.Where(n => n.Kind != NodeKind.Service).ShouldAllBe(n => n.Health == null && n.Scope == null);
    }

    [Fact]
    public void Sample_drops_an_edge_nobody_called_for_five_minutes()
    {
        // Arrange
        var fixture = new TrackerFixture().With("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 3, 0)));
        fixture.Tracker.Sample();

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        fixture.Tracker.Sample();

        // Assert
        fixture.Tracker.GetGraph().Edges.ShouldBeEmpty();
        fixture.Tracker.GetGraph().Nodes.ShouldBeEmpty();
    }

    [Fact]
    public void Sample_keeps_an_edge_as_long_as_its_total_keeps_growing()
    {
        // Arrange
        var fixture = new TrackerFixture().With("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 3, 0)));
        fixture.Tracker.Sample();
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        fixture.Replace("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 4, 0)));
        fixture.Tracker.Sample();

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        fixture.Tracker.Sample();

        // Assert
        fixture.Tracker.GetGraph().Edges.Single().Calls.ShouldBe(4);
    }

    [Fact]
    public void Sample_brings_an_edge_back_when_it_is_called_again()
    {
        // Arrange
        var fixture = new TrackerFixture().With("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 3, 0)));
        fixture.Tracker.Sample();
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        fixture.Tracker.Sample();

        // Act
        fixture.Replace("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 9, 0)));
        fixture.Tracker.Sample();

        // Assert
        fixture.Tracker.GetGraph().Edges.Single().Calls.ShouldBe(9);
    }

    [Fact]
    public void Sample_ignores_instances_that_are_gone_or_report_no_edges()
    {
        // Arrange
        var fixture = new TrackerFixture()
            .With("shop", Instance("orders", "orders-1", Edge("DB", "mssql://db1/orders", 3, 0), lifecycle: InstanceLifecycle.Gone))
            .With("shop", Instance("payments", "payments-1"));

        // Act
        fixture.Tracker.Sample();

        // Assert
        fixture.Tracker.GetGraph().Edges.ShouldBeEmpty();
    }

    private static EdgeReport Edge(string type, string target, long calls, long errors) =>
        new(type, target, calls, errors, calls * 10.0, [calls, 0]);

    private static InstanceInfo Instance(
        string service, string id, params EdgeReport[] edges) => Instance(service, id, HealthState.Healthy, InstanceLifecycle.Running, edges);

    private static InstanceInfo Instance(string service, string id, EdgeReport edge, InstanceLifecycle lifecycle) =>
        Instance(service, id, HealthState.Healthy, lifecycle, [edge]);

    private static InstanceInfo Instance(string service, string id, HealthState health) => Instance(service, id, health, InstanceLifecycle.Running, []);

    private static InstanceInfo Instance(string service, string id, EdgeReport edge, HealthState health) =>
        Instance(service, id, health, InstanceLifecycle.Running, [edge]);

    private static InstanceInfo Instance(string service, string id, HealthState health, InstanceLifecycle lifecycle, EdgeReport[] edges) =>
        new(id, service, lifecycle, health, new Dictionary<string, string>(), Edges: edges.Length == 0 ? null : edges);

    private sealed class TrackerFixture
    {
        private readonly FakeScopeRegistry _registry = new();
        private readonly Dictionary<string, List<InstanceInfo>> _instances = [];

        public TrackerFixture()
        {
            Tracker = new TopologyTracker(_registry, new TopologyOptions(), Clock);
        }

        public FakeTimeProvider Clock { get; } = new();

        public TopologyTracker Tracker { get; }

        public TrackerFixture With(string scope, InstanceInfo instance)
        {
            if (!_instances.TryGetValue(scope, out var list))
            {
                _instances[scope] = list = [];
            }

            list.Add(instance);
            _registry.WithInstance(scope, instance);
            return this;
        }

        /// <summary>Replaces what an instance reports, as a new snapshot does.</summary>
        public void Replace(string scope, InstanceInfo instance)
        {
            _instances[scope].RemoveAll(i => i.Id == instance.Id);
            _instances[scope].Add(instance);
            _registry.WithEmptyScope(scope);
            foreach (var kept in _instances[scope])
            {
                _registry.WithInstance(scope, kept);
            }
        }
    }
}
