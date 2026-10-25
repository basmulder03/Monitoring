using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Registry;

public class ScopeRegistryTests
{
    [Fact]
    public void ApplySnapshot_replaces_all_instances_of_the_scope()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a", "b");

        // Act
        fixture.Registry.ApplySnapshot(new Snapshot(2, "shop", [RegistryFixture.Instance("c")]));

        // Assert
        fixture.IdsOf("shop").ShouldBe(["c"]);
    }

    [Fact]
    public void ApplySnapshot_leaves_other_scopes_untouched()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a").WithSnapshot("billing", 1, "x");

        // Act
        fixture.Registry.ApplySnapshot(new Snapshot(2, "shop", []));

        // Assert
        fixture.IdsOf("billing").ShouldBe(["x"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void ApplySnapshot_acknowledges_its_own_sequence(long sequence)
    {
        // Arrange
        var fixture = new RegistryFixture();

        // Act
        var result = fixture.Registry.ApplySnapshot(new Snapshot(sequence, "shop", []));

        // Assert
        result.ShouldBe(new ApplyResult(ApplyOutcome.Applied, sequence));
    }

    [Fact]
    public void ApplySnapshot_accepts_a_lower_sequence_after_an_agent_restart()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 500, "a");

        // Act
        var result = fixture.Registry.ApplySnapshot(new Snapshot(1, "shop", [RegistryFixture.Instance("b")]));

        // Assert
        result.AcknowledgedSequence.ShouldBe(1);
        fixture.IdsOf("shop").ShouldBe(["b"]);
    }

    [Fact]
    public void ApplyDelta_adds_and_updates_upserted_instances()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a");
        var updated = RegistryFixture.Instance("a", health: HealthState.Unhealthy);
        var added = RegistryFixture.Instance("b");

        // Act
        fixture.Registry.ApplyDelta("shop", new Delta(2, [updated, added], []));

        // Assert
        fixture.Registry.GetInstances("shop").ShouldBe([updated, added]);
    }

    [Fact]
    public void ApplyDelta_shows_removed_instances_as_gone()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a", "b");

        // Act
        fixture.Registry.ApplyDelta("shop", new Delta(2, [], ["a"]));

        // Assert
        fixture.Registry.GetInstances("shop").Single(i => i.Id == "a").Lifecycle.ShouldBe(InstanceLifecycle.Gone);
    }

    [Fact]
    public void ApplyDelta_ignores_removal_of_an_unknown_instance()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a");

        // Act
        fixture.Registry.ApplyDelta("shop", new Delta(2, [], ["missing"]));

        // Assert
        fixture.IdsOf("shop").ShouldBe(["a"]);
    }

    [Fact]
    public void ApplyDelta_with_the_next_sequence_is_applied_and_acknowledged()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 5, "a");

        // Act
        var result = fixture.Registry.ApplyDelta("shop", new Delta(6, [], []));

        // Assert
        result.ShouldBe(new ApplyResult(ApplyOutcome.Applied, 6));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    [InlineData(1)]
    public void ApplyDelta_with_an_old_sequence_is_ignored_as_duplicate(long sequence)
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 5, "a");

        // Act
        var result = fixture.Registry.ApplyDelta("shop", new Delta(sequence, [RegistryFixture.Instance("b")], []));

        // Assert
        result.ShouldBe(new ApplyResult(ApplyOutcome.Duplicate, 5));
        fixture.IdsOf("shop").ShouldBe(["a"]);
    }

    [Fact]
    public void ApplyDelta_with_a_skipped_sequence_is_applied_and_reports_the_gap()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 5, "a");

        // Act
        var result = fixture.Registry.ApplyDelta("shop", new Delta(8, [RegistryFixture.Instance("b")], []));

        // Assert
        result.ShouldBe(new ApplyResult(ApplyOutcome.AppliedWithGap, 8));
        fixture.IdsOf("shop").ShouldBe(["a", "b"]);
    }

    [Fact]
    public void ApplyDelta_before_any_snapshot_asks_for_one()
    {
        // Arrange
        var fixture = new RegistryFixture();

        // Act
        var result = fixture.Registry.ApplyDelta("shop", new Delta(1, [RegistryFixture.Instance("a")], []));

        // Assert
        result.ShouldBe(new ApplyResult(ApplyOutcome.SnapshotRequired, null));
        fixture.IdsOf("shop").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    public void PurgeExpired_drops_gone_instances_only_after_the_retention(int minutesSinceRemoval, bool stillListed)
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a");
        fixture.Registry.ApplyDelta("shop", new Delta(2, [], ["a"]));
        fixture.Clock.Advance(TimeSpan.FromMinutes(minutesSinceRemoval));

        // Act
        fixture.Registry.PurgeExpired();

        // Assert
        fixture.IdsOf("shop").Contains("a").ShouldBe(stillListed);
    }

    [Fact]
    public void PurgeExpired_keeps_instances_that_are_not_gone()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a");
        fixture.Clock.Advance(TimeSpan.FromDays(1));

        // Act
        fixture.Registry.PurgeExpired();

        // Assert
        fixture.IdsOf("shop").ShouldBe(["a"]);
    }

    [Fact]
    public void GetScopeNames_lists_every_scope_that_sent_a_snapshot_ordered_by_name()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a").WithSnapshot("billing", 1);

        // Act
        var names = fixture.Registry.GetScopeNames();

        // Assert
        names.ShouldBe(["billing", "shop"]);
    }

    [Fact]
    public void GetScopeNames_does_not_list_a_scope_known_only_from_a_delta()
    {
        // Arrange
        var fixture = new RegistryFixture();
        fixture.Registry.ApplyDelta("shop", new Delta(1, [RegistryFixture.Instance("a")], []));

        // Act
        var names = fixture.Registry.GetScopeNames();

        // Assert
        names.ShouldBeEmpty();
    }

    [Fact]
    public void GetScopeLabels_returns_the_labels_of_the_latest_snapshot()
    {
        // Arrange
        var fixture = new RegistryFixture();
        fixture.Registry.ApplySnapshot(new Snapshot(1, "shop", [], new Dictionary<string, string> { ["environment"] = "prod" }));

        // Act
        var labels = fixture.Registry.GetScopeLabels("shop");

        // Assert
        labels["environment"].ShouldBe("prod");
        labels.Count.ShouldBe(1);
    }

    [Fact]
    public void GetScopeLabels_follows_a_change_announced_by_a_newer_snapshot()
    {
        // Arrange
        var fixture = new RegistryFixture();
        fixture.Registry.ApplySnapshot(new Snapshot(1, "shop", [], new Dictionary<string, string> { ["environment"] = "test" }));
        fixture.Registry.ApplySnapshot(new Snapshot(2, "shop", [], new Dictionary<string, string> { ["environment"] = "prod", ["site"] = "a" }));

        // Act
        var labels = fixture.Registry.GetScopeLabels("shop");

        // Assert
        labels["environment"].ShouldBe("prod");
        labels["site"].ShouldBe("a");
    }

    [Fact]
    public void GetScopeLabels_keeps_the_labels_when_deltas_arrive()
    {
        // Arrange
        var fixture = new RegistryFixture();
        fixture.Registry.ApplySnapshot(new Snapshot(1, "shop", [], new Dictionary<string, string> { ["environment"] = "prod" }));
        fixture.Registry.ApplyDelta("shop", new Delta(2, [RegistryFixture.Instance("a")], []));

        // Act
        var labels = fixture.Registry.GetScopeLabels("shop");

        // Assert
        labels["environment"].ShouldBe("prod");
    }

    [Fact]
    public void GetScopeLabels_is_empty_for_a_snapshot_without_labels()
    {
        // Arrange
        var fixture = new RegistryFixture().WithSnapshot("shop", 1, "a");

        // Act
        var labels = fixture.Registry.GetScopeLabels("shop");

        // Assert
        labels.ShouldBeEmpty();
    }

    [Fact]
    public void GetScopeLabels_is_empty_for_an_unknown_scope()
    {
        // Arrange
        var fixture = new RegistryFixture();

        // Act
        var labels = fixture.Registry.GetScopeLabels("nowhere");

        // Assert
        labels.ShouldBeEmpty();
    }

    [Fact]
    public void GetScopeLabels_is_not_changed_by_later_changes_to_the_dictionary_that_was_sent()
    {
        // Arrange
        var fixture = new RegistryFixture();
        var sent = new Dictionary<string, string> { ["environment"] = "prod" };
        fixture.Registry.ApplySnapshot(new Snapshot(1, "shop", [], sent));
        sent["environment"] = "changed";

        // Act
        var labels = fixture.Registry.GetScopeLabels("shop");

        // Assert
        labels["environment"].ShouldBe("prod");
    }

    [Fact]
    public void GetInstances_returns_an_empty_list_for_an_unknown_scope()
    {
        // Arrange
        var fixture = new RegistryFixture();

        // Act
        var instances = fixture.Registry.GetInstances("nowhere");

        // Assert
        instances.ShouldBeEmpty();
    }

    /// <summary>Builds a registry with a fake clock and optional starting state.</summary>
    private sealed class RegistryFixture
    {
        public RegistryFixture()
        {
            Clock = new FakeTimeProvider();
            Registry = new ScopeRegistry(new RegistryOptions(), Clock);
        }

        public FakeTimeProvider Clock { get; }

        public IScopeRegistry Registry { get; }

        /// <summary>Applies a snapshot with running, healthy instances of the given ids.</summary>
        public RegistryFixture WithSnapshot(string scope, long sequence, params string[] ids)
        {
            Registry.ApplySnapshot(new Snapshot(sequence, scope, [.. ids.Select(id => Instance(id))]));
            return this;
        }

        public IReadOnlyList<string> IdsOf(string scope) => [.. Registry.GetInstances(scope).Select(i => i.Id)];

        public static InstanceInfo Instance(string id, HealthState health = HealthState.Healthy) =>
            new(id, "orders", InstanceLifecycle.Running, health, new Dictionary<string, string>());
    }
}
