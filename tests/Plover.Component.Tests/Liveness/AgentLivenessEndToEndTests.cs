using System.Net.Sockets;
using System.Text.Json;
using Plover.Component.Tests.Support;
using Plover.Protocol.Framing;
using Plover.Protocol.Messages;
using Plover.Protocol.Versioning;
using Shouldly;
using Xunit;

namespace Plover.Component.Tests.Liveness;

/// <summary>What the dashboard of a real controller shows when the agent of a scope goes away.</summary>
public class AgentLivenessEndToEndTests
{
    private const string Token = "enroll";

    private static ControllerHost Controller() =>
        new ControllerHost().AcceptingEnrollment(Token)
            .With("Controller:Liveness:HeartbeatInterval", "00:00:00.200")
            .With("Controller:Alerting:StartupGrace", "00:00:00")
            .With("Controller:Alerting:ScopeUnknownWait", "00:00:01")
            .With("Controller:Alerting:EvaluationInterval", "00:00:00.200")
            .With("Controller:Dashboard:PushInterval", "00:00:00.200");

    [Fact]
    public async Task A_scope_turns_unknown_when_its_agent_is_stopped()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = Controller().Start();
        var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("health").GetString().ShouldBe("Healthy"));

        // Act
        await agent.DisposeAsync();

        // Assert
        await Eventually.AssertAsync(() =>
        {
            var scope = Scope(http, controller);
            scope.GetProperty("health").GetString().ShouldBe("Unknown");
            scope.GetProperty("agentConnected").GetBoolean().ShouldBeFalse();
            scope.GetProperty("lastSeen").ValueKind.ShouldBe(JsonValueKind.String);
        });
    }

    [Fact]
    public async Task The_last_known_instances_stay_visible_while_the_agent_is_away()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = Controller().Start();
        var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("instances").GetInt32().ShouldBe(1));

        // Act
        await agent.DisposeAsync();

        // Assert
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("agentConnected").GetBoolean().ShouldBeFalse());
        Scope(http, controller).GetProperty("instances").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task An_alert_fires_when_the_agent_stays_away_and_clears_when_it_returns()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = Controller().Start();
        var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("health").GetString().ShouldBe("Healthy"));
        await agent.DisposeAsync();

        // Act / Assert: it fires
        await Eventually.AssertAsync(() =>
        {
            var alert = Alerts(http, controller).Single(a => a.GetProperty("rule").GetString() == "scope-unknown");
            alert.GetProperty("subject").GetString().ShouldBe("shop");
            alert.GetProperty("severity").GetString().ShouldBe("Critical");
        });

        // Act / Assert: the agent comes back and it clears
        await using var returned = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await Eventually.AssertAsync(() =>
        {
            Scope(http, controller).GetProperty("health").GetString().ShouldBe("Healthy");
            Alerts(http, controller).Any(a => a.GetProperty("rule").GetString() == "scope-unknown").ShouldBeFalse();
        });
    }

    [Fact]
    public async Task A_connection_that_goes_silent_without_closing_turns_the_scope_unknown_and_is_closed_by_the_controller()
    {
        // Arrange: a client that says hello and sends its state, then neither heartbeats nor disconnects
        await using var controller = Controller().Start();
        using var client = new TcpClient();
        await client.ConnectAsync(controller.AgentEndPoint, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await FrameCodec.WriteAsync(stream, new Hello(ProtocolVersion.Current, "silent", "agent", null, new Dictionary<string, string>(), Token, "shop"), ct: TestContext.Current.CancellationToken);
        (await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken)).ShouldBeOfType<Config>();
        await FrameCodec.WriteAsync(stream, new Snapshot(1, "shop", []), ct: TestContext.Current.CancellationToken);
        (await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken)).ShouldBeOfType<Ack>();
        using var http = Web();
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("agentConnected").GetBoolean().ShouldBeTrue());

        // Act: nothing is sent for longer than three heartbeats (0.6 s)
        // Assert
        await Eventually.AssertAsync(() => Scope(http, controller).GetProperty("agentConnected").GetBoolean().ShouldBeFalse());
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        (await FrameCodec.ReadAsync(stream, ct: guard.Token)).ShouldBeNull();
    }

    [Fact]
    public async Task A_client_that_keeps_sending_heartbeats_keeps_the_scope_alive()
    {
        // Arrange
        await using var controller = Controller().Start();
        using var client = new TcpClient();
        await client.ConnectAsync(controller.AgentEndPoint, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await FrameCodec.WriteAsync(stream, new Hello(ProtocolVersion.Current, "steady", "agent", null, new Dictionary<string, string>(), Token, "shop"), ct: TestContext.Current.CancellationToken);
        (await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken)).ShouldBeOfType<Config>();
        await FrameCodec.WriteAsync(stream, new Snapshot(1, "shop", []), ct: TestContext.Current.CancellationToken);
        (await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken)).ShouldBeOfType<Ack>();
        using var http = Web();

        // Act: heartbeats for well over the timeout
        for (var i = 1; i <= 10; i++)
        {
            await Task.Delay(200, TestContext.Current.CancellationToken);
            await FrameCodec.WriteAsync(stream, new Heartbeat(i, Plover.Protocol.Health.HealthState.Healthy), ct: TestContext.Current.CancellationToken);
        }

        // Assert
        Scope(http, controller).GetProperty("agentConnected").GetBoolean().ShouldBeTrue();
    }

    private static HttpClient Web() => new(new HttpClientHandler { UseProxy = false });

    private static JsonElement Overview(HttpClient http, ControllerHost controller) =>
        JsonDocument.Parse(http.GetStringAsync($"{controller.WebAddress}/api/overview", TestContext.Current.CancellationToken).GetAwaiter().GetResult()).RootElement;

    private static JsonElement Scope(HttpClient http, ControllerHost controller) => Overview(http, controller).GetProperty("scopes")[0];

    private static IEnumerable<JsonElement> Alerts(HttpClient http, ControllerHost controller) =>
        Overview(http, controller).GetProperty("alerts").EnumerateArray().ToList();
}
