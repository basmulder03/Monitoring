using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Budgets;

/// <summary>
/// The scale the design is sized for (docs/design.md, "Memory and storage"): what a controller holds for thousands of instances with dozens of series each,
/// and how big a frame is. The limits are set well above what was measured, to catch a change that multiplies the cost and not to track every byte.
/// </summary>
public class ScaleBudgetTests(ITestOutputHelper output)
{
    private const int SeriesPerInstance = 50;

    private static InstanceInfo Instance(int number) => new(
        $"orders-{number:D5}-7d9f8b6c5d-x{number % 10}k2p",
        $"service-{number % 100}",
        InstanceLifecycle.Running,
        HealthState.Healthy,
        new Dictionary<string, string> { ["app"] = $"service-{number % 100}", ["pod-template-hash"] = "7d9f8b6c5d" },
        Version: "1.2.3",
        Metrics: [.. Enumerable.Range(0, SeriesPerInstance).Select(i => new MetricSeries($"http.server.request.duration.series{i}", new Dictionary<string, string> { ["route"] = "/orders" }, i * 1.5))]);

    [Fact]
    public void Five_thousand_instances_with_fifty_series_each_fit_in_the_memory_budget()
    {
        // Arrange
        const int instances = 5000;
        var registry = new ScopeRegistry(new RegistryOptions(), new FakeTimeProvider());
        var before = GC.GetTotalMemory(forceFullCollection: true);

        // Act: 50 scopes of 100 instances, as agents would send them
        var snapshots = Enumerable.Range(0, 50).Select(scope =>
            new Snapshot(1, $"scope-{scope}", [.. Enumerable.Range(scope * 100, 100).Select(Instance)])).ToList();
        foreach (var snapshot in snapshots)
        {
            registry.ApplySnapshot(snapshot);
        }

        snapshots.Clear();
        var held = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(registry);

        // Assert
        var megabytes = held / 1_048_576.0;
        output.WriteLine($"{instances} instances x {SeriesPerInstance} series: {megabytes:F1} MB");
        megabytes.ShouldBeLessThan(150, "the design expects the latest values of 250,000 series to take a few tens of MB");
    }

    [Fact]
    public async Task A_snapshot_of_a_thousand_instances_with_fifty_series_each_fits_in_a_frame_with_room_to_spare()
    {
        // Arrange
        var snapshot = new Snapshot(1, "scope", [.. Enumerable.Range(0, 1000).Select(Instance)]);
        using var stream = new MemoryStream();

        // Act
        await FrameCodec.WriteAsync(stream, snapshot, ct: TestContext.Current.CancellationToken);

        // Assert
        output.WriteLine($"1000 instances x {SeriesPerInstance} series: {stream.Length / 1024.0:F0} KB per snapshot (limit {FrameCodec.DefaultMaxFrameBytes / 1024} KB)");
        stream.Length.ShouldBeLessThan(FrameCodec.DefaultMaxFrameBytes / 2, "a namespace of a thousand instances must fit with room to spare");
    }
}
