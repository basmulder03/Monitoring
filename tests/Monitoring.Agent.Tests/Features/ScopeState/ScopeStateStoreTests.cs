using System.Threading.Channels;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ScopeState;

public class ScopeStateStoreTests
{
    [Fact]
    public void Publish_sends_the_changes_as_one_delta_with_the_next_sequence()
    {
        // Arrange
        var fixture = new StoreFixture();
        var store = fixture.Build();
        using var subscription = store.Subscribe();
        store.Upsert(StoreFixture.Instance("b"));
        store.Upsert(StoreFixture.Instance("a"));
        store.Remove("a");
        store.Upsert(StoreFixture.Instance("c"));

        // Act
        store.Publish();

        // Assert: "a" was added and removed in the same batch, so only its removal is reported
        var delta = StoreFixture.Next(subscription);
        delta.Sequence.ShouldBe(1);
        delta.Upserted.Select(i => i.Id).ShouldBe(["b", "c"]);
        delta.Removed.ShouldBe(["a"]);
    }

    [Fact]
    public void Publish_sends_nothing_when_nothing_changed()
    {
        // Arrange
        var store = new StoreFixture().Build();
        using var subscription = store.Subscribe();

        // Act
        store.Publish();

        // Assert
        subscription.Deltas.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Publish_numbers_each_delta_one_higher_than_the_last()
    {
        // Arrange
        var store = new StoreFixture().Build();
        using var subscription = store.Subscribe();

        // Act
        store.Upsert(StoreFixture.Instance("a"));
        store.Publish();
        store.Upsert(StoreFixture.Instance("b"));
        store.Publish();

        // Assert
        StoreFixture.Next(subscription).Sequence.ShouldBe(1);
        StoreFixture.Next(subscription).Sequence.ShouldBe(2);
    }

    [Fact]
    public void Publish_does_not_repeat_changes_that_were_already_sent()
    {
        // Arrange
        var store = new StoreFixture().Build();
        using var subscription = store.Subscribe();
        store.Upsert(StoreFixture.Instance("a"));
        store.Publish();
        StoreFixture.Next(subscription);

        // Act
        store.Publish();

        // Assert
        subscription.Deltas.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Remove_ignores_an_unknown_instance()
    {
        // Arrange
        var store = new StoreFixture().Build();
        using var subscription = store.Subscribe();

        // Act
        store.Remove("missing");
        store.Publish();

        // Assert
        subscription.Deltas.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Upsert_after_remove_in_the_same_batch_keeps_the_instance_and_reports_no_removal()
    {
        // Arrange
        var store = new StoreFixture().WithInstances("a").Build();
        using var subscription = store.Subscribe();
        store.Remove("a");
        store.Upsert(StoreFixture.Instance("a", HealthState.Degraded));

        // Act
        store.Publish();

        // Assert
        var delta = StoreFixture.Next(subscription);
        delta.Removed.ShouldBeEmpty();
        delta.Upserted.Single().Health.ShouldBe(HealthState.Degraded);
    }

    [Fact]
    public void CreateSnapshot_lists_every_instance_ordered_by_id_under_the_scope_name()
    {
        // Arrange
        var store = new StoreFixture().WithScope("shop").WithInstances("b", "a").Build();

        // Act
        var snapshot = store.CreateSnapshot();

        // Assert
        snapshot.ScopeName.ShouldBe("shop");
        snapshot.Instances.Select(i => i.Id).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void CreateSnapshot_carries_the_current_sequence_without_consuming_one()
    {
        // Arrange
        var store = new StoreFixture().WithInstances("a").Build();
        store.Publish();

        // Act
        var first = store.CreateSnapshot();
        var second = store.CreateSnapshot();

        // Assert
        (first.Sequence, second.Sequence).ShouldBe((1L, 1L));
    }

    [Fact]
    public void Subscribe_starts_from_a_snapshot_at_the_current_sequence_and_delivers_only_later_deltas()
    {
        // Arrange
        var store = new StoreFixture().Build();
        store.Upsert(StoreFixture.Instance("a"));
        store.Publish();
        store.Upsert(StoreFixture.Instance("b"));

        // Act
        using var subscription = store.Subscribe();
        store.Publish();

        // Assert: the snapshot already holds both instances; the delta that follows is number 2
        subscription.Initial.Sequence.ShouldBe(1);
        subscription.Initial.Instances.Select(i => i.Id).ShouldBe(["a", "b"]);
        StoreFixture.Next(subscription).Sequence.ShouldBe(2);
    }

    [Fact]
    public void Publish_delivers_the_same_delta_to_every_subscriber()
    {
        // Arrange
        var store = new StoreFixture().Build();
        using var first = store.Subscribe();
        using var second = store.Subscribe();
        store.Upsert(StoreFixture.Instance("a"));

        // Act
        store.Publish();

        // Assert
        StoreFixture.Next(first).Sequence.ShouldBe(1);
        StoreFixture.Next(second).Sequence.ShouldBe(1);
    }

    [Fact]
    public void Dispose_stops_delivery_to_the_subscription()
    {
        // Arrange
        var store = new StoreFixture().Build();
        var subscription = store.Subscribe();
        subscription.Dispose();
        store.Upsert(StoreFixture.Instance("a"));

        // Act
        store.Publish();

        // Assert
        subscription.Deltas.TryRead(out _).ShouldBeFalse();
        subscription.Deltas.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_cuts_off_a_subscriber_that_fell_too_far_behind_without_affecting_others()
    {
        // Arrange
        var store = new StoreFixture().WithBuffer(1).Build();
        using var slow = store.Subscribe();
        store.Upsert(StoreFixture.Instance("a"));
        store.Publish();
        using var fast = store.Subscribe();
        store.Upsert(StoreFixture.Instance("b"));
        store.Publish();
        StoreFixture.Next(fast);

        // Act
        store.Upsert(StoreFixture.Instance("c"));
        store.Publish();

        // Assert
        StoreFixture.Next(fast).Sequence.ShouldBe(3);
        StoreFixture.Next(slow).Sequence.ShouldBe(1);
        var read = async () => await slow.Deltas.WaitToReadAsync(TestContext.Current.CancellationToken);
        await read.ShouldThrowAsync<InvalidOperationException>();
    }

    /// <summary>Builds a store with a chosen scope name, buffer size and starting instances.</summary>
    private sealed class StoreFixture
    {
        private readonly ScopeStateOptions _options = new() { ScopeName = "scope" };
        private readonly List<string> _ids = [];

        public StoreFixture WithScope(string name)
        {
            _options.ScopeName = name;
            return this;
        }

        public StoreFixture WithBuffer(int size)
        {
            _options.SubscriberBuffer = size;
            return this;
        }

        public StoreFixture WithInstances(params string[] ids)
        {
            _ids.AddRange(ids);
            return this;
        }

        /// <summary>Creates the store and adds the starting instances without publishing them.</summary>
        public ScopeStateStore Build()
        {
            var store = new ScopeStateStore(_options);
            foreach (var id in _ids)
            {
                store.Upsert(Instance(id));
            }

            return store;
        }

        public static InstanceInfo Instance(string id, HealthState health = HealthState.Healthy) =>
            new(id, "orders", InstanceLifecycle.Running, health, new Dictionary<string, string>());

        /// <summary>Reads the delta that is already waiting; fails when there is none.</summary>
        public static Delta Next(StateSubscription subscription) =>
            subscription.Deltas.TryRead(out var delta) ? delta : throw new InvalidOperationException("No delta was published.");
    }
}
