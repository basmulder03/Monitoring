using Plover.Controller.Features.Alerting;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class QueueRuleTests
{
    private static InstanceInfo Pod(string id, string queue, double? depth = null, double? consumers = null, double? dead = null)
    {
        var labels = new Dictionary<string, string> { ["queue"] = queue };
        var metrics = new List<MetricSeries>();
        if (depth is { } d) { metrics.Add(new MetricSeries(QueueRule.DepthMetric, labels, d)); }
        if (consumers is { } c) { metrics.Add(new MetricSeries(QueueRule.ConsumersMetric, labels, c)); }
        if (dead is { } x) { metrics.Add(new MetricSeries(QueueRule.DeadLettersMetric, labels, x)); }
        return new InstanceInfo(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), Metrics: metrics);
    }

    private static QueueRule DeadLetters(FakeScopeRegistry registry) =>
        new("queue-dead-letters", AlertSeverity.Warning, TimeSpan.Zero, registry, q => q.DeadLetters > 0);

    private static QueueRule NoConsumers(FakeScopeRegistry registry) =>
        new("queue-without-consumers", AlertSeverity.Critical, TimeSpan.FromMinutes(2), registry, q => q.Depth > 0 && q.Consumers == 0);

    [Fact]
    public void A_queue_with_dead_letters_is_named()
    {
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", "orders", depth: 0, dead: 4)).WithInstance("shop", Pod("b", "audit", dead: 0));

        DeadLetters(registry).FindFailingSubjects().ShouldBe(["shop/orders/orders"]);
    }

    [Fact]
    public void A_queue_with_waiting_messages_and_no_consumers_is_named_but_one_that_is_empty_or_unknown_is_not()
    {
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Pod("a", "stuck", depth: 9, consumers: 0))
            .WithInstance("shop", Pod("b", "idle", depth: 0, consumers: 0))
            .WithInstance("shop", Pod("c", "unknown", depth: 9));

        NoConsumers(registry).FindFailingSubjects().ShouldBe(["shop/orders/stuck"]);
    }

    [Fact]
    public void Replicas_ask_the_same_broker_so_a_consumer_seen_by_one_of_them_counts()
    {
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Pod("a", "orders", depth: 9, consumers: 0))
            .WithInstance("shop", Pod("b", "orders", depth: 9, consumers: 1));

        NoConsumers(registry).FindFailingSubjects().ShouldBeEmpty();
    }
}
