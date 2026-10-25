using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Plover.Agent.Features.Configuration;
using Plover.Agent.Features.ScopeState;
using Plover.Agent.Features.SidecarConnections;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Protocol.Versioning;
using Plover.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Budgets;

/// <summary>
/// One agent with many sidecars connected over real TCP and the real clock: how long it takes until all of them are registered, and until a change of
/// health in every application has reached the agent's view. The limits are many times above what was measured, so a shared runner's noise does not
/// fail the build but a change that makes a sidecar cost an order of magnitude more does. The test prints what it measured.
/// </summary>
public class SidecarFanInBudgetTests(ITestOutputHelper output)
{
    private const int Sidecars = 300;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Three_hundred_sidecars_register_and_report_a_change_in_health_quickly()
    {
        // Arrange
        var store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "shop" });
        for (var i = 0; i < Sidecars; i++)
        {
            store.Upsert(new InstanceInfo(Id(i), "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));
        }

        var options = new SidecarConnectionsOptions { Port = 0, ListenAddress = "127.0.0.1", HeartbeatInterval = TimeSpan.FromSeconds(5) };
        var handler = new SidecarConnectionHandler(store, new AgentConfiguration(NullLogger<AgentConfiguration>.Instance), options, TimeProvider.System, NullLogger<SidecarConnectionHandler>.Instance);
        var listener = new SidecarListener(handler, options, NullLogger<SidecarListener>.Instance);
        var port = listener.Start().Port;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var listening = listener.RunAsync(stop.Token);

        var health = new HealthBox();
        var registered = Enumerable.Range(0, Sidecars).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var sidecars = Enumerable.Range(0, Sidecars).Select(i => RunSidecarAsync(port, Id(i), health, registered[i], stop.Token)).ToArray();

        // Act: all sidecars connect, then every application turns degraded
        var connecting = Stopwatch.StartNew();
        await Task.WhenAll(registered.Select(r => r.Task)).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var registration = connecting.Elapsed;

        var changing = Stopwatch.StartNew();
        health.Value = HealthState.Degraded;
        while (store.CreateSnapshot().Instances.Any(i => i.Health != HealthState.Degraded))
        {
            changing.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), "the change reaches the agent's view");
            await Task.Delay(10, Ct);
        }

        var change = changing.Elapsed;

        await stop.CancelAsync();
        await Task.WhenAll(sidecars.Append(listening)).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Assert
        output.WriteLine($"{Sidecars} sidecars: all registered after {registration.TotalMilliseconds:F0} ms, a change in every application in the agent's view after {change.TotalMilliseconds:F0} ms");
        PerfResults.Record("agent.sidecars.register", registration.TotalMilliseconds, "ms");
        PerfResults.Record("agent.sidecars.change", change.TotalMilliseconds, "ms");
        registration.ShouldBeLessThan(TimeSpan.FromSeconds(10), "every sidecar is greeted and asked for its health at once");
        change.ShouldBeLessThan(TimeSpan.FromSeconds(10), "a heartbeat that shows another health is followed by a pull of the health");
    }

    private static string Id(int number) => $"orders-{number:D4}";

    /// <summary>A sidecar as the library link behaves: says hello, answers the pulls with the health it has and sends heartbeats that show it.</summary>
    private static async Task RunSidecarAsync(int port, string instance, HealthBox health, TaskCompletionSource registered, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, ct);
            await using var stream = client.GetStream();
            var writing = new SemaphoreSlim(1);
            async Task SendAsync(Message message)
            {
                await writing.WaitAsync(ct);
                try
                {
                    await FrameCodec.WriteAsync(stream, message, ct: ct);
                }
                finally
                {
                    writing.Release();
                }
            }

            await SendAsync(new Hello(ProtocolVersion.Current, instance, "orders", "1.0", new Dictionary<string, string>()));
            var heartbeats = Task.Run(async () =>
            {
                long sequence = 0;
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                while (await timer.WaitForNextTickAsync(ct))
                {
                    await SendAsync(new Heartbeat(++sequence, health.Value));
                }
            }, ct);

            while (await FrameCodec.ReadAsync(stream, ct: ct) is { } message)
            {
                if (message is not Pull pull)
                {
                    continue;
                }

                if (pull.Kind == PullKind.Health)
                {
                    await SendAsync(new PullResult(pull.RequestId, PullKind.Health, 0, ReportPayload.Write(new HealthReport(health.Value, []))));
                    registered.TrySetResult();
                }
                else if (pull.Kind == PullKind.Packages)
                {
                    await SendAsync(new PullResult(pull.RequestId, PullKind.Packages, 0, ReportPayload.Write(new PackagesReport(".NET 10", []))));
                }
            }

            await heartbeats;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The test is over.
        }
        catch (Exception e)
        {
            registered.TrySetException(e);
        }
    }

    private sealed class HealthBox
    {
        private volatile int _value = (int)HealthState.Healthy;

        public HealthState Value
        {
            get => (HealthState)_value;
            set => _value = (int)value;
        }
    }
}
