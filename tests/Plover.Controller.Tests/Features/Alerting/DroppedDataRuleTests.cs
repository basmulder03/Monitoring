using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class DroppedDataRuleTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly ScopeRegistry _registry;
    private readonly DroppedDataRule _rule;
    private long _sequence;

    public DroppedDataRuleTests()
    {
        _registry = new ScopeRegistry(new RegistryOptions(), _clock);
        _rule = new DroppedDataRule(_registry, new AlertingOptions(), _clock);
    }

    private IReadOnlyList<string> Report(double dropped)
    {
        _registry.ApplySnapshot(new Snapshot(++_sequence, "shop", [new InstanceInfo(
            "orders-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(),
            Metrics: [new MetricSeries(DroppedDataRule.MetricName, new Dictionary<string, string>(), dropped)])]));
        return [.. _rule.FindFailingSubjects()];
    }

    [Fact]
    public void An_instance_that_throws_away_many_spans_within_the_window_is_named()
    {
        // Arrange
        Report(0);
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var subjects = Report(250);

        // Assert
        subjects.ShouldBe(["shop/orders/orders-1"]);
    }

    [Fact]
    public void A_few_dropped_spans_and_a_count_from_before_the_controller_watched_are_not_a_problem()
    {
        // Arrange
        var first = Report(50_000);
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var second = Report(50_040);

        // Assert
        first.ShouldBeEmpty();
        second.ShouldBeEmpty();
    }
}
