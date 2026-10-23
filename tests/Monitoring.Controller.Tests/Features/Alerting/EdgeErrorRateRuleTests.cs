using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class EdgeErrorRateRuleTests
{
    private sealed class Fixture
    {
        private long _sequence;

        public Fixture()
        {
            Registry = new ScopeRegistry(new RegistryOptions(), Clock);
            Rule = new EdgeErrorRateRule(Registry, new AlertingOptions(), Clock);
        }

        public FakeTimeProvider Clock { get; } = new();

        public ScopeRegistry Registry { get; }

        public EdgeErrorRateRule Rule { get; }

        /// <summary>The instances of the service report these totals, and the rule looks.</summary>
        public IReadOnlyList<string> Report(params (string Instance, long Calls, long Errors)[] instances)
        {
            Registry.ApplySnapshot(new Snapshot(++_sequence, "shop", [.. instances.Select(i => new InstanceInfo(
                i.Instance, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(),
                Edges: [new EdgeReport("HTTP", "payments:8080", i.Calls, i.Errors, 0, [])]))]));
            return [.. Rule.FindFailingSubjects()];
        }
    }

    [Fact]
    public void What_happened_before_the_controller_watched_does_not_count()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var subjects = fixture.Report(("a", 10_000, 9_000));

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void A_dependency_whose_calls_mostly_fail_within_the_window_is_named_for_the_service()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Report(("a", 100, 0));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var subjects = fixture.Report(("a", 130, 20));

        // Assert
        subjects.ShouldBe(["shop/orders -> payments:8080"]);
    }

    [Fact]
    public void The_calls_of_all_instances_of_the_service_count_together()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Report(("a", 0, 0), ("b", 0, 0));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: neither has the minimum alone, both together do
        var subjects = fixture.Report(("a", 12, 8), ("b", 12, 8));

        // Assert
        subjects.ShouldBe(["shop/orders -> payments:8080"]);
    }

    [Fact]
    public void Few_calls_and_a_small_share_of_failures_say_nothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Report(("a", 0, 0));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var fewCalls = fixture.Report(("a", 10, 10));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var smallShare = fixture.Report(("a", 210, 30));

        // Assert
        fewCalls.ShouldBeEmpty();
        smallShare.ShouldBeEmpty();
    }

    [Fact]
    public void Failures_older_than_the_window_stop_counting()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Report(("a", 0, 0));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Report(("a", 50, 40)).ShouldNotBeEmpty();

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));
        fixture.Report(("a", 50, 40));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var subjects = fixture.Report(("a", 50, 40));

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void An_application_that_restarted_starts_its_counts_again_without_a_false_alarm()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Report(("a", 5_000, 100));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act: the totals went down, because the application started again
        var subjects = fixture.Report(("a", 40, 1));

        // Assert
        subjects.ShouldBeEmpty();
    }
}
