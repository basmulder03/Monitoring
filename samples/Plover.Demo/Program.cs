// A real controller with its dashboard, fed by made-up agents that play a short incident again and again, so the dashboard can be looked at, tested by hand or recorded
// (docs/scripts/record-dashboard.mjs) without a cluster: dotnet run --project samples/Plover.Demo [port]  (default 5080), then open http://127.0.0.1:5080.
// The agents speak the real protocol over TCP; only the pods behind them are invented.
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Controller.Features.AgentLink;
using Plover.Controller.Features.Security;
using Plover.Controller.Hosting;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;

const string Token = "demo";
var port = args.Length > 0 ? args[0] : "5080";
var step = TimeSpan.FromSeconds(4);

var settings = new Dictionary<string, string?>
{
    ["Controller:Listen:Address"] = "127.0.0.1",
    ["Controller:Listen:Port"] = "0",
    ["Controller:Web:Address"] = "127.0.0.1",
    ["Controller:Web:Port"] = port,
    ["Controller:AgentTokens:0:Scope"] = AgentTokenEntry.AnyScope,
    ["Controller:AgentTokens:0:TokenHash"] = AgentTokenHash.Compute(Token),
    ["Controller:Alerting:StartupGrace"] = "00:00:01",
    ["Controller:Alerting:InstanceUnhealthyWait"] = "00:00:04",
    // Removed pods go away soon, so the list stays short while the incident is played again and again.
    ["Controller:Registry:GoneRetention"] = "00:00:08",
    ["Controller:Registry:PurgeInterval"] = "00:00:02",
};
// With DEMO_SIGNED_IN=1 the dashboard believes the headers X-Forwarded-User and X-Forwarded-Groups from this machine, as it would behind a proxy that signs people in; a group named ops
// makes an operator. That is what the recording of silencing an alert uses (docs/scripts/record-dashboard.mjs). Without it the dashboard is open and shows no buttons to change anything.
if (Environment.GetEnvironmentVariable("DEMO_SIGNED_IN") == "1")
{
    settings["Controller:Web:Auth:TrustedProxies:0"] = "127.0.0.1";
    settings["Controller:Web:Auth:OperatorGroup"] = "ops";
}

using var controller = ControllerComposition.CreateHost([], settings, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
await controller.StartAsync();
var endpoint = controller.Services.GetRequiredService<IAgentListener>().LocalEndPoint!;
Console.WriteLine($"Dashboard on http://127.0.0.1:{port}; agents connect to {endpoint}");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

var agents = new[]
{
    new DemoAgent("shop", "prod", "a", [("orders", 2), ("payments", 2), ("catalog", 1)]),
    new DemoAgent("billing", "prod", "a", [("invoices", 2), ("ledger", 1)]),
    new DemoAgent("shop-test", "test", "b", [("orders", 1), ("payments", 1)]),
};
try
{
    await Task.WhenAll(agents.Select(a => a.RunAsync(endpoint, Token, step, stop.Token)));
}
catch (OperationCanceledException)
{
    // Stopped by the user.
}

await controller.StopAsync();

/// <summary>One scope's agent: connects, sends its pods, then plays the incident (degrade, fail, roll out, recover) in a loop.</summary>
internal sealed class DemoAgent(string scope, string environment, string datacenter, (string Service, int Pods)[] services)
{
    private readonly Dictionary<string, InstanceInfo> _pods = [];
    private long _sequence;
    private int _generation;
    private string _victimId = "";

    public async Task RunAsync(System.Net.IPEndPoint endpoint, string token, TimeSpan step, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(endpoint, ct);
        await using var stream = tcp.GetStream();
        var writing = new SemaphoreSlim(1);
        async Task SendAsync(Message message)
        {
            await writing.WaitAsync(ct);
            try { await FrameCodec.WriteAsync(stream, message, ct: ct); } finally { writing.Release(); }
        }

        await SendAsync(new Hello(ProtocolVersion(), $"agent-{scope}", "agent", null, new Dictionary<string, string>(), token, scope));
        _ = await FrameCodec.ReadAsync(stream, ct: ct);

        foreach (var (service, pods) in services)
        {
            for (var i = 1; i <= pods; i++) { Set(Pod(service, $"{service}-{i}", HealthState.Healthy)); }
        }

        await SendAsync(new Snapshot(++_sequence, scope, [.. _pods.Values], new Dictionary<string, string> { ["environment"] = environment, ["datacenter"] = datacenter }));

        // Acknowledgements and requests are read and dropped; the controller only needs the agent to be there.
        var reading = Task.Run(async () => { while (await FrameCodec.ReadAsync(stream, ct: ct) is not null) { } }, ct);
        var beats = Task.Run(async () =>
        {
            long beat = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct)) { await SendAsync(new Heartbeat(++beat, HealthState.Healthy)); }
        }, ct);

        // Only the first service of the first scope and of the second one is hit, so the overview shows a mix.
        var victim = services[0].Service;
        _victimId = $"{victim}-2";
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(step * 2, ct);
            if (scope == "billing") { await Change(SendAsync, Pod("invoices", "invoices-1", HealthState.Degraded, "queue", "backlog is growing")); }
            if (scope == "shop") { await Change(SendAsync, Pod(victim, _victimId, HealthState.Degraded, "database", "answers in 850 ms")); }
            await Task.Delay(step, ct);
            if (scope == "shop") { await Change(SendAsync, Pod(victim, _victimId, HealthState.Unhealthy, "database", "connection timed out")); }
            await Task.Delay(step, ct);
            if (scope == "shop")
            {
                // A rollout: the new pod starts, the failing one terminates and goes, and the new one is the one that fails next time.
                var next = Pod(victim, $"{victim}-{3 + _generation++}", HealthState.Healthy);
                await Change(SendAsync, next with { Lifecycle = InstanceLifecycle.Starting });
                await Change(SendAsync, _pods[_victimId] with { Lifecycle = InstanceLifecycle.Terminating });
                await Task.Delay(step / 2, ct);
                await Change(SendAsync, next);
                _pods.Remove(_victimId);
                await SendAsync(new Delta(++_sequence, [], [_victimId]));
                _victimId = next.Id;
            }

            await Task.Delay(step, ct);
            if (scope == "billing") { await Change(SendAsync, Pod("invoices", "invoices-1", HealthState.Healthy)); }
        }

        await Task.WhenAll(reading, beats);
    }

    private static int ProtocolVersion() => Plover.Protocol.Versioning.ProtocolVersion.Current;

    private void Set(InstanceInfo pod) => _pods[pod.Id] = pod;

    private async Task Change(Func<Message, Task> send, InstanceInfo pod)
    {
        Set(pod);
        await send(new Delta(++_sequence, [pod], []));
    }

    private InstanceInfo Pod(string service, string id, HealthState health, string? failingCheck = null, string? why = null)
    {
        var checks = new List<HealthCheckResult> { new("database", HealthState.Healthy), new("queue", HealthState.Healthy), new("disk space", HealthState.Healthy) };
        if (failingCheck is not null)
        {
            checks[checks.FindIndex(c => c.Name == failingCheck)] = new HealthCheckResult(failingCheck, health, why);
        }

        var calls = 1000 + (_sequence * 120);
        return new InstanceInfo(
            id, service, InstanceLifecycle.Running, health,
            new Dictionary<string, string> { ["app"] = service, ["pod-template-hash"] = "7d9f8b6c5d" },
            Version: "2.4.1",
            Checks: checks,
            Metrics: [new MetricSeries("http.server.request.duration", new Dictionary<string, string> { ["route"] = "/" }, 12 + id.Length * 2)],
            Edges: service switch
            {
                "orders" => [new EdgeReport("HTTP", "http://payments:8080", calls, health == HealthState.Unhealthy ? calls / 3 : 4, calls * 14.0, [calls / 2, calls / 3, calls / 6]), new EdgeReport("DB", "orders-db:5432/orders", calls * 2, health == HealthState.Unhealthy ? calls : 0, calls * 20.0, [calls, calls / 2, calls / 2])],
                "payments" => [new EdgeReport("MQ", "payments.queue", calls, 0, calls * 5.0, [calls, 0, 0])],
                "invoices" => [new EdgeReport("HTTP", "http://ledger:8080", calls, 2, calls * 9.0, [calls / 2, calls / 3, calls / 6])],
                _ => null,
            });
    }
}
