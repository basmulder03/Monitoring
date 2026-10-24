using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Budgets;

/// <summary>
/// A controller that restarts while many agents are connected: they all come back at once and send their whole scope, and the new controller starts empty.
/// The size is that of <see cref="ScaleBudgetTests"/> (5,000 instances with 50 series each, here as 100 agents). The limit is many times above what was measured,
/// so a shared runner's noise does not fail the build but a change that makes taking in a snapshot an order of magnitude slower does. The test prints what it measured.
/// </summary>
// Runs alone, like the other budgets: it measures time, and a test beside it on a shared runner would make that noise.
[Collection("Scale budgets")]
public class RecoveryBudgetTests(ITestOutputHelper output)
{
    private const int Agents = 100;
    private const int InstancesPerAgent = 50;
    private const int SeriesPerInstance = 50;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Scope(int agent) => $"scope-{agent:D3}";

    // A token names one scope, so every scope needs its own.
    private static string Token(string scope) => $"token-{scope}";

    private static InstanceInfo Instance(int agent, int number) => new(
        $"orders-{agent:D3}-{number:D3}",
        $"service-{number % 10}",
        InstanceLifecycle.Running,
        HealthState.Healthy,
        new Dictionary<string, string> { ["app"] = $"service-{number % 10}" },
        Version: "1.2.3",
        Metrics: [.. Enumerable.Range(0, SeriesPerInstance).Select(i => new MetricSeries($"http.server.request.duration.series{i}", new Dictionary<string, string> { ["route"] = "/orders" }, i * 1.5))]);

    [Fact]
    public async Task A_hundred_agents_with_five_thousand_instances_come_back_to_a_restarted_controller_quickly()
    {
        // Arrange: a controller with all agents connected and their scopes known
        var snapshots = Enumerable.Range(0, Agents).Select(agent =>
            new Snapshot(1, Scope(agent), [.. Enumerable.Range(0, InstancesPerAgent).Select(n => Instance(agent, n))])).ToArray();
        var first = Start();
        var connections = await Task.WhenAll(Enumerable.Range(0, Agents).Select(agent => ConnectAsync(first.Endpoint, snapshots[agent])));
        first.Registry.GetScopeNames().Count.ShouldBe(Agents);

        // Act: the controller goes away, a new one starts empty, and every agent reconnects and sends its scope again
        foreach (var connection in connections)
        {
            connection.Dispose();
        }

        first.Provider.Dispose();
        var second = Start();
        var recovering = Stopwatch.StartNew();
        var again = await Task.WhenAll(Enumerable.Range(0, Agents).Select(agent => ConnectAsync(second.Endpoint, snapshots[agent])));
        var recovery = recovering.Elapsed;

        // Assert
        output.WriteLine($"{Agents} agents x {InstancesPerAgent} instances x {SeriesPerInstance} series: a restarted controller has all of it again after {recovery.TotalMilliseconds:F0} ms");
        Enumerable.Range(0, Agents).Sum(agent => second.Registry.GetInstances(Scope(agent)).Count).ShouldBe(Agents * InstancesPerAgent);
        recovery.ShouldBeLessThan(TimeSpan.FromSeconds(10), "every agent connects, authenticates and has its snapshot acknowledged");

        foreach (var connection in again)
        {
            connection.Dispose();
        }

        second.Provider.Dispose();
    }

    /// <summary>Starts a controller with the agent link on a free loopback port and a token for every scope.</summary>
    private static (ServiceProvider Provider, IScopeRegistry Registry, IPEndPoint Endpoint) Start()
    {
        var provider = new ServiceCollection()
            .AddRegistry()
            .AddLiveness()
            .AddConfiguration()
            .AddSecurity(o =>
            {
                foreach (var agent in Enumerable.Range(0, Agents))
                {
                    o.AgentTokens.Add(new AgentTokenEntry(Scope(agent), AgentTokenHash.Compute(Token(Scope(agent)))));
                }
            })
            .AddAgentLink(o =>
            {
                o.ListenAddress = "127.0.0.1";
                o.Port = 0;
            })
            .BuildServiceProvider();
        var listener = provider.GetRequiredService<IAgentListener>();
        var endpoint = listener.Start();
        _ = listener.RunAsync(CancellationToken.None);
        return (provider, provider.GetRequiredService<IScopeRegistry>(), endpoint);
    }

    /// <summary>Connects as an agent, says hello, sends the snapshot and waits for it to be acknowledged. Returns the connection, to keep it open.</summary>
    private static async Task<TcpClient> ConnectAsync(IPEndPoint endpoint, Snapshot snapshot)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint, Ct);
        var stream = tcp.GetStream();
        await FrameCodec.WriteAsync(stream, new Hello(1, $"agent-{snapshot.ScopeName}", "agent", null, new Dictionary<string, string>(), Token(snapshot.ScopeName)), ct: Ct);
        (await FrameCodec.ReadAsync(stream, ct: Ct)).ShouldBeOfType<Config>();
        await FrameCodec.WriteAsync(stream, snapshot, ct: Ct);
        (await FrameCodec.ReadAsync(stream, ct: Ct)).ShouldBe(new Ack(snapshot.Sequence));
        return tcp;
    }
}
