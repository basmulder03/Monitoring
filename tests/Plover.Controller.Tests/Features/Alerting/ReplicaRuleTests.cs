using Plover.Controller.Features.Alerting;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class ReplicaRuleTests
{
    /// <summary>An agent that says it is connected to a replica or not, and how far behind the replica is.</summary>
    internal static InstanceInfo Agent(string id, params (string Replica, bool Connected, double? Lag)[] replicas)
    {
        List<MetricSeries> metrics = [];
        foreach (var (replica, connected, lag) in replicas)
        {
            var labels = new Dictionary<string, string> { ["replica"] = replica };
            metrics.Add(new MetricSeries(ReplicaMetrics.Connected, labels, connected ? 1 : 0));
            if (lag is { } value)
            {
                metrics.Add(new MetricSeries(ReplicaMetrics.Lag, labels, value));
            }
        }

        return new InstanceInfo(id, ReplicaMetrics.AgentService, InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), Metrics: metrics);
    }

    private static ReplicaRule Behind(FakeScopeRegistry registry) =>
        new("replica-behind", registry, TimeSpan.FromMinutes(1), report => report.Lag >= 25);

    private static ReplicaRule Unreachable(FakeScopeRegistry registry) =>
        new("replica-unreachable", registry, TimeSpan.FromMinutes(2), report => !report.Connected);

    [Fact]
    public void A_replica_that_is_far_behind_for_any_agent_is_named_once()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Agent("a-1", ("c0:9440", true, 3), ("c1:9440", true, 40)))
            .WithInstance("blog", Agent("a-2", ("c0:9440", true, 1), ("c1:9440", true, 90)));

        // Act
        var subjects = Behind(registry).FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["c1:9440"]);
    }

    [Fact]
    public void A_replica_that_an_agent_is_not_connected_to_is_named_as_unreachable_but_not_as_behind()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Agent("a-1", ("c0:9440", true, 0), ("c1:9440", false, null)));

        // Act
        var unreachable = Unreachable(registry).FindFailingSubjects();
        var behind = Behind(registry).FindFailingSubjects();

        // Assert
        unreachable.ShouldBe(["c1:9440"]);
        behind.ShouldBeEmpty();
    }

    [Fact]
    public void Replicas_that_are_fine_and_instances_that_are_not_agents_or_are_gone_are_ignored()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Agent("a-1", ("c0:9440", true, 2)))
            .WithInstance("shop", Agent("a-2", ("c9:9440", false, null)) with { Lifecycle = InstanceLifecycle.Gone })
            .WithInstance("shop", Agent("a-3", ("c8:9440", false, null)) with { ServiceName = "orders" });

        // Act
        var subjects = Unreachable(registry).FindFailingSubjects().Concat(Behind(registry).FindFailingSubjects());

        // Assert
        subjects.ShouldBeEmpty();
    }
}
