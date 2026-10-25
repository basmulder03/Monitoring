using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.ChangeMarkers;
using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.ChangeMarkers;

public class ChangeTrackerTests
{
    private static InstanceInfo Pod(string id, string? version, InstanceLifecycle lifecycle = InstanceLifecycle.Running) =>
        new(id, "orders", lifecycle, HealthState.Healthy, new Dictionary<string, string>(), version);

    private sealed class Fixture
    {
        private long _sequence;

        public Fixture(int maxMarkers = 200)
        {
            Registry = new ScopeRegistry(new RegistryOptions(), Clock);
            Tracker = new ChangeTracker(Registry, new ChangeMarkersOptions { MaxMarkers = maxMarkers }, Clock);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

        public ScopeRegistry Registry { get; }

        public ChangeTracker Tracker { get; }

        public Fixture Report(params InstanceInfo[] instances)
        {
            Registry.ApplySnapshot(new Snapshot(++_sequence, "shop", instances));
            Tracker.Sample();
            return this;
        }
    }

    [Fact]
    public void The_first_version_seen_is_the_baseline_and_not_a_change()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        fixture.Report(Pod("a", "1.0"));

        // Assert
        fixture.Tracker.GetRecent().ShouldBeEmpty();
    }

    [Fact]
    public void A_new_version_is_a_change_from_the_one_before()
    {
        // Arrange
        var fixture = new Fixture().Report(Pod("a", "1.0"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        fixture.Report(Pod("b", "1.1"));

        // Assert
        var marker = fixture.Tracker.GetRecent().Single();
        marker.Scope.ShouldBe("shop");
        marker.Service.ShouldBe("orders");
        marker.From.ShouldBe("1.0");
        marker.To.ShouldBe("1.1");
        marker.At.ShouldBe(fixture.Clock.GetUtcNow());
    }

    [Fact]
    public void The_same_version_again_and_instances_going_away_are_not_changes()
    {
        // Arrange
        var fixture = new Fixture().Report(Pod("a", "1.0"), Pod("b", "1.0"));

        // Act
        fixture.Report(Pod("a", "1.0"), Pod("b", "1.0", InstanceLifecycle.Gone));
        fixture.Report(Pod("a", "1.0"));

        // Assert
        fixture.Tracker.GetRecent().ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_version_is_ignored()
    {
        // Arrange
        var fixture = new Fixture().Report(Pod("a", "1.0"));

        // Act
        fixture.Report(Pod("a", null));

        // Assert
        fixture.Tracker.GetRecent().ShouldBeEmpty();
    }

    [Fact]
    public void A_rollout_that_runs_two_versions_is_one_change_per_new_version_and_newest_comes_first()
    {
        // Arrange
        var fixture = new Fixture().Report(Pod("a", "1.0"));

        // Act
        fixture.Report(Pod("a", "1.0"), Pod("b", "1.1"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        fixture.Report(Pod("b", "1.1"), Pod("c", "1.2"));

        // Assert
        fixture.Tracker.GetRecent().Select(marker => marker.To).ShouldBe(["1.2", "1.1"]);
    }

    [Fact]
    public void Only_the_newest_markers_are_kept()
    {
        // Arrange
        var fixture = new Fixture(maxMarkers: 2).Report(Pod("a", "1.0"));

        // Act
        fixture.Report(Pod("a", "1.1"));
        fixture.Report(Pod("a", "1.2"));
        fixture.Report(Pod("a", "1.3"));

        // Assert
        fixture.Tracker.GetRecent().Select(marker => marker.To).ShouldBe(["1.3", "1.2"]);
    }

    [Fact]
    public void A_value_that_is_not_positive_is_refused()
    {
        // Act
        var add = () => new ServiceCollection().AddChangeMarkers(options => options.MaxMarkers = 0);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
