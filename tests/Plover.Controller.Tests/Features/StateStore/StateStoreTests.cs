using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.ChangeMarkers;
using Plover.Controller.Features.Registry;
using Plover.Controller.Features.StateStore;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.StateStore;

public class StateStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("state-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }

    private IStateStore Open(string? file = null, Action<string>? onFailure = null)
    {
        var path = file ?? Path.Combine(_folder, "state.db");
        var provider = new ServiceCollection().AddStateStore(o => o.SqlitePath = path).BuildServiceProvider();
        return provider.GetRequiredService<IStateStore>();
    }

    private static IStateStore Memory() => new ServiceCollection().AddStateStore().BuildServiceProvider().GetRequiredService<IStateStore>();

    [Fact]
    public void Without_a_file_the_logs_live_in_memory_in_order_and_are_trimmed_to_what_is_kept()
    {
        var store = Memory();

        foreach (var value in new[] { "a", "b", "c", "d" })
        {
            store.Append("log", value, keep: 3);
        }

        store.Read("log").ShouldBe(["b", "c", "d"]);
        store.Read("other").ShouldBeEmpty();
    }

    [Fact]
    public void With_a_file_the_logs_survive_opening_the_store_again_and_keep_their_order_and_limit()
    {
        var path = Path.Combine(_folder, "state.db");
        var first = Open(path);
        foreach (var value in new[] { "a", "b", "c", "d" })
        {
            first.Append("log", value, keep: 3);
        }

        first.Append("other", "x", keep: 3);
        ((IDisposable)first).Dispose();

        var second = Open(path);

        second.Read("log").ShouldBe(["b", "c", "d"]);
        second.Read("other").ShouldBe(["x"]);
    }

    [Fact]
    public void A_store_that_cannot_be_written_tells_why_and_still_remembers_for_the_life_of_the_process()
    {
        // Arrange
        var path = Path.Combine(_folder, "state.db");
        var problems = new List<string>();
        using var store = new SqliteStateStore(path, problems.Add);
        using (var other = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            other.Open();
            using var drop = other.CreateCommand();
            drop.CommandText = "DROP TABLE log";
            drop.ExecuteNonQuery();
        }

        // Act
        store.Append("log", "a", keep: 5);

        // Assert
        problems.ShouldNotBeEmpty();
        store.Read("log").ShouldBe(["a"]);
    }

    [Fact]
    public void Change_markers_survive_a_restart_of_the_controller()
    {
        // Arrange
        var path = Path.Combine(_folder, "state.db");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var registry = new ScopeRegistry(new RegistryOptions(), clock);
        var first = Open(path);
        var tracker = new ChangeTracker(registry, new ChangeMarkersOptions(), clock, first);
        registry.ApplySnapshot(new Snapshot(1, "shop", [new InstanceInfo("a", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0")]));
        tracker.Sample();
        registry.ApplySnapshot(new Snapshot(2, "shop", [new InstanceInfo("a", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "2.0")]));
        tracker.Sample();
        ((IDisposable)first).Dispose();

        // Act: a new controller process with the same file
        var restarted = new ChangeTracker(new ScopeRegistry(new RegistryOptions(), clock), new ChangeMarkersOptions(), clock, Open(path));

        // Assert
        var marker = restarted.GetRecent().Single();
        (marker.Scope, marker.Service, marker.From, marker.To).ShouldBe(("shop", "orders", "1.0", "2.0"));
    }
}
