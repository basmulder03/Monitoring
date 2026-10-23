using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Tests.Shared;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Versioning;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.AgentLink;

public class AgentConnectionHandlerTests
{
    private const string Token = "secret";
    private const string Scope = "shop";

    [Fact]
    public async Task A_request_for_history_reaches_the_agent_and_is_answered_with_what_it_sends_back()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        var asking = session.Connections.GetMetricHistoryAsync(Scope, "orders-1", TestContext.Current.CancellationToken);
        var request = (await session.ReceiveAsync()).ShouldBeOfType<HistoryRequest>();
        await session.SendAsync(new HistoryResult(request.RequestId, "{\"intervalSeconds\":15,\"series\":[]}"));

        // Assert
        request.InstanceId.ShouldBe("orders-1");
        (await asking).ShouldBe("{\"intervalSeconds\":15,\"series\":[]}");
    }

    [Fact]
    public async Task A_request_for_history_is_not_sent_to_an_agent_that_does_not_understand_it()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();
        await session.SendAsync(HandlerFixture.Hello(Token, version: ProtocolVersion.HistoryRequests - 1));
        (await session.ReceiveAsync()).ShouldBeOfType<Config>();

        // Act
        var answer = await session.Connections.GetMetricHistoryAsync(Scope, "orders-1", TestContext.Current.CancellationToken);

        // Assert
        answer.ShouldBeNull();
    }

    [Fact]
    public async Task A_request_for_history_gives_up_when_the_agent_goes_away()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);
        var asking = session.Connections.GetMetricHistoryAsync(Scope, "orders-1", TestContext.Current.CancellationToken);
        (await session.ReceiveAsync()).ShouldBeOfType<HistoryRequest>();

        // Act
        session.Disconnect();

        // Assert
        (await asking).ShouldBeNull();
    }

    [Fact]
    public async Task A_request_for_history_of_a_scope_without_an_agent_is_answered_with_nothing()
    {
        // Arrange
        var connections = new AgentConnections(new AgentLinkOptions());

        // Act
        var answer = await connections.GetMetricHistoryAsync("nowhere", "orders-1", TestContext.Current.CancellationToken);

        // Assert
        answer.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_answers_a_valid_hello_with_the_configuration()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).WithReplicas("tcp://c1:9440", "tcp://c2:9440").Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(Token));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Config>().ControllerReplicas.ShouldBe(["tcp://c1:9440", "tcp://c2:9440"]);
    }

    [Fact]
    public async Task HandleAsync_answers_a_hello_with_the_settings_of_the_central_configuration_for_that_agent()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope);
        var session = fixture.Build();
        fixture.Configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 }, "overrides": [ { "match": { "scope": "shop" }, "set": { "edgeFlushSeconds": 20 } } ] }"""));

        // Act
        await session.SendAsync(HandlerFixture.Hello(Token));
        var reply = await session.ReceiveAsync();

        // Assert
        var config = reply.ShouldBeOfType<Config>();
        config.PullSeconds.ShouldBe(30);
        config.EdgeFlushSeconds.ShouldBe(20);
        config.Version.ShouldNotStartWith("built-in");
    }

    [Fact]
    public async Task HandleAsync_sends_a_new_config_when_the_central_configuration_changes()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope);
        var session = await fixture.Build().HandshakeAsync(Token);

        // Act
        fixture.Configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 99 } }"""));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Config>().PullSeconds.ShouldBe(99);
    }

    [Fact]
    public async Task HandleAsync_notes_the_configuration_version_an_agent_says_it_uses_and_forgets_it_when_the_agent_leaves()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope);
        var session = await fixture.Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy, "v7"));
        await session.SendAsync(new Snapshot(0, Scope, []));
        await session.ReceiveAsync();
        var whileConnected = session.Applied.StatusOf(Scope, new Dictionary<string, string>()).Applied;
        session.Disconnect();
        await session.Handling;

        // Assert
        whileConnected.ShouldBe("v7");
        session.Applied.StatusOf(Scope, new Dictionary<string, string>()).Applied.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_keeps_serving_state_after_it_pushed_a_new_config()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope);
        var session = await fixture.Build().HandshakeAsync(Token);
        fixture.Configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 99 } }"""));
        (await session.ReceiveAsync()).ShouldBeOfType<Config>();

        // Act
        await session.SendAsync(new Snapshot(0, Scope, [HandlerFixture.Instance("pod-1")]));
        var ack = await session.ReceiveAsync();

        // Assert
        ack.ShouldBeOfType<Ack>();
        session.Registry.GetInstances(Scope).Single().Id.ShouldBe("pod-1");
    }

    [Fact]
    public async Task HandleAsync_does_not_send_a_config_when_the_configuration_is_replaced_by_the_same_content()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope);
        fixture.Configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));
        var session = await fixture.Build().HandshakeAsync(Token);

        // Act
        fixture.Configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        session.Shutdown();
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Theory]
    [InlineData(ProtocolVersion.Current - 1)]
    [InlineData(ProtocolVersion.Current)]
    [InlineData(ProtocolVersion.Current + 1)]
    public async Task HandleAsync_accepts_adjacent_protocol_versions(int version)
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(Token, version));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Config>();
    }

    [Theory]
    [InlineData(ProtocolVersion.Current - 2)]
    [InlineData(ProtocolVersion.Current + 2)]
    public async Task HandleAsync_closes_the_connection_for_an_unsupported_protocol_version(int version)
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(Token, version));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    public async Task HandleAsync_closes_the_connection_when_authentication_fails(string? token)
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(token));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_when_the_first_message_is_not_a_hello()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_when_no_hello_arrives_in_time()
    {
        // Arrange
        var fixture = new HandlerFixture().WithToken(Token, Scope).WithHelloTimeout(TimeSpan.FromSeconds(10));
        var session = fixture.Build();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_applies_a_snapshot_to_the_registry_and_acknowledges_it()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Snapshot(7, Scope, [HandlerFixture.Instance("pod-1")]));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBe(new Ack(7));
        session.Registry.GetInstances(Scope).Select(i => i.Id).ShouldBe(["pod-1"]);
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_when_a_snapshot_names_another_scope()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Snapshot(1, "billing", [HandlerFixture.Instance("pod-1")]));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
        session.Registry.GetScopeNames().ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_applies_a_delta_to_the_authenticated_scope_and_acknowledges_it()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);
        await session.SendAsync(new Snapshot(1, Scope, [HandlerFixture.Instance("pod-1")]));
        await session.ReceiveAsync();

        // Act
        await session.SendAsync(new Delta(2, [HandlerFixture.Instance("pod-2")], []));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBe(new Ack(2));
        session.Registry.GetInstances(Scope).Select(i => i.Id).ShouldBe(["pod-1", "pod-2"]);
    }

    [Fact]
    public async Task HandleAsync_asks_for_a_snapshot_when_a_delta_arrives_first()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Delta(5, [HandlerFixture.Instance("pod-1")], []));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBe(new Ack(0, SnapshotRequired: true));
    }

    [Fact]
    public async Task HandleAsync_acknowledges_a_repeated_delta_with_the_sequence_already_applied()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);
        await session.SendAsync(new Snapshot(4, Scope, []));
        await session.ReceiveAsync();

        // Act
        await session.SendAsync(new Delta(3, [], []));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBe(new Ack(4));
    }

    [Fact]
    public async Task HandleAsync_accepts_heartbeats_without_replying_or_closing()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        await session.SendAsync(new Snapshot(1, Scope, []));
        var reply = await session.ReceiveAsync();

        // Assert: the first reply is the snapshot's ack, so the heartbeat produced none
        reply.ShouldBe(new Ack(1));
    }

    [Fact]
    public async Task HandleAsync_ends_quietly_on_a_goodbye()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Goodbye("agent-1"));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    public static TheoryData<Message> MessagesAnAgentMustNotSend() => new()
    {
        new Hello(1, "agent-1", "agent", null, new Dictionary<string, string>(), Token),
        new Config("v", []),
        new Pull(1, PullKind.Info, 0),
        new Ack(1),
    };

    [Theory]
    [MemberData(nameof(MessagesAnAgentMustNotSend))]
    public async Task HandleAsync_closes_the_connection_on_a_message_an_agent_must_not_send(Message message)
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(message);
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_refuses_a_first_frame_larger_than_a_hello_needs_before_reading_it()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();
        var header = new byte[FrameCodec.HeaderSize];
        BinaryPrimitives.WriteInt32BigEndian(header, FrameCodec.HandshakeMaxFrameBytes + 1);

        // Act
        await session.SendRawAsync(header);
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_accepts_the_snapshot_of_a_scope_with_a_thousand_instances_of_fifty_series_each()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);
        IReadOnlyList<MetricSeries> series = [.. Enumerable.Range(0, 50).Select(i => new MetricSeries($"http.server.request.duration.series{i}", new Dictionary<string, string> { ["route"] = "/orders" }, i))];
        var instances = Enumerable.Range(0, 1000).Select(i => HandlerFixture.Instance($"orders-{i:D5}-7d9f8b6c5d-x7k2p") with { Metrics = series }).ToList();

        // Act
        await session.SendAsync(new Snapshot(1, Scope, instances));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Ack>().Sequence.ShouldBe(1);
        session.Registry.GetInstances(Scope).Count.ShouldBe(1000);
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_on_a_malformed_frame_without_throwing()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendRawAsync(HandlerFixture.FrameOf("not json"));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_completes_when_the_agent_disconnects()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        session.Disconnect();

        // Assert
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_completes_without_throwing_when_the_controller_shuts_down()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        session.Shutdown();

        // Assert
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandleAsync_tells_the_agent_which_heartbeat_interval_to_use()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).WithHeartbeat(TimeSpan.FromSeconds(2.2), 3).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(Token));
        var reply = await session.ReceiveAsync();

        // Assert: rounded up, so the agent is never slower than the controller expects
        reply.ShouldBeOfType<Config>().HeartbeatSeconds.ShouldBe(3);
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_connected_once_it_is_accepted()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.HandshakeAsync(Token);

        // Assert
        session.Liveness.Events.ShouldBe(["connected:shop"]);
    }

    [Fact]
    public async Task HandleAsync_does_not_report_an_agent_that_was_refused()
    {
        // Arrange
        var session = new HandlerFixture().WithToken(Token, Scope).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello("wrong"));
        await session.ReceiveAsync();
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        session.Liveness.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_heard_from_for_a_heartbeat()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act: the snapshot that follows is answered, which shows the heartbeat before it was handled
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        await session.SendAsync(new Snapshot(1, Scope, []));
        await session.ReceiveAsync();

        // Assert: one for the heartbeat, one for the snapshot
        session.Liveness.Events.Count(e => e == "seen:shop").ShouldBe(2);
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_heard_from_for_a_snapshot()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendAsync(new Snapshot(1, Scope, []));
        await session.ReceiveAsync();

        // Assert
        session.Liveness.Events.Count(e => e == "seen:shop").ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_gone_when_it_disconnects()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        session.Disconnect();
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        session.Liveness.Events.ShouldBe(["connected:shop", "disconnected:shop"]);
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_gone_when_the_connection_breaks()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        await session.SendRawAsync(HandlerFixture.FrameOf("not json"));
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        session.Liveness.Events.Last().ShouldBe("disconnected:shop");
    }

    [Fact]
    public async Task HandleAsync_reports_the_agent_as_gone_when_the_controller_shuts_down()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).Build().HandshakeAsync(Token);

        // Act
        session.Shutdown();
        await session.Handling.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        session.Liveness.Events.Last().ShouldBe("disconnected:shop");
    }

    [Fact]
    public async Task HandleAsync_closes_a_connection_that_stays_silent_for_the_heartbeat_timeout()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).WithHeartbeat(TimeSpan.FromSeconds(5), 3).Build().HandshakeAsync(Token);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        session.Clock.Advance(TimeSpan.FromSeconds(15));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
        session.Liveness.Events.Last().ShouldBe("disconnected:shop");
    }

    [Fact]
    public async Task HandleAsync_keeps_a_connection_open_that_keeps_sending_heartbeats()
    {
        // Arrange
        var session = await new HandlerFixture().WithToken(Token, Scope).WithHeartbeat(TimeSpan.FromSeconds(5), 3).Build().HandshakeAsync(Token);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        session.Clock.Advance(TimeSpan.FromSeconds(10));
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        session.Clock.Advance(TimeSpan.FromSeconds(10));
        await session.SendAsync(new Snapshot(1, Scope, []));
        var reply = await session.ReceiveAsync();

        // Assert: 20 s passed in total, but never 15 s without a message
        reply.ShouldBe(new Ack(1));
    }

    /// <summary>Builds a handler served over an in-memory connection, with a fake authenticator and a real registry.</summary>
    private sealed class HandlerFixture
    {
        private readonly Dictionary<string, string> _scopeByToken = [];
        private readonly AgentLinkOptions _options = new();
        private readonly LivenessOptions _livenessOptions = new();
        private AppliedConfigurations? _applied;
        private AgentConnections? _connections;

        public FakeTimeProvider Clock { get; } = new();

        public AgentConnections Connections => _connections ??= new AgentConnections(_options);

        public FakeAgentLiveness Liveness { get; } = new();

        public EffectiveConfiguration Configuration { get; } = new(NullLogger<EffectiveConfiguration>.Instance);

        public AppliedConfigurations Applied => _applied ??= new AppliedConfigurations(Configuration);

        public HandlerFixture WithHeartbeat(TimeSpan interval, int missed)
        {
            _livenessOptions.HeartbeatInterval = interval;
            _livenessOptions.MissedHeartbeats = missed;
            return this;
        }

        public HandlerFixture WithToken(string token, string scope)
        {
            _scopeByToken[token] = scope;
            return this;
        }

        public HandlerFixture WithReplicas(params string[] addresses)
        {
            _options.ControllerReplicas = [.. addresses];
            return this;
        }

        public HandlerFixture WithHelloTimeout(TimeSpan timeout)
        {
            _options.HelloTimeout = timeout;
            return this;
        }

        /// <summary>Starts the handler on the server end of a new connection and returns the agent's end.</summary>
        public Session Build()
        {
            var registry = new ScopeRegistry(new RegistryOptions(), Clock);
            var handler = new AgentConnectionHandler(
                new FakeAuthenticator(_scopeByToken), registry, Liveness, _livenessOptions, Configuration, Applied, _options, Clock, NullLogger<AgentConnectionHandler>.Instance, Connections);
            var (server, client) = DuplexStream.CreatePair();
            var shutdown = new CancellationTokenSource();

            // Starts running on this thread up to its first await, so its hello timer exists when this returns.
            var handling = ServeAsync(handler, server, shutdown.Token);
            return new Session(client, registry, handling, shutdown, Liveness, Clock, Configuration, Applied, Connections);
        }

        public static Hello Hello(string? token, int version = ProtocolVersion.Current) =>
            new(version, "agent-1", "agent", null, new Dictionary<string, string>(), token);

        public static InstanceInfo Instance(string id) =>
            new(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>());

        /// <summary>A frame with a valid length prefix around arbitrary text.</summary>
        public static byte[] FrameOf(string payloadText)
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            var frame = new byte[FrameCodec.HeaderSize + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
            payload.CopyTo(frame, FrameCodec.HeaderSize);
            return frame;
        }

        /// <summary>Closes the server end when handling ends, as the listener does when it disposes the socket.</summary>
        private static async Task ServeAsync(AgentConnectionHandler handler, DuplexStream server, CancellationToken ct)
        {
            try
            {
                await handler.HandleAsync(server, ct);
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    /// <summary>The agent's end of a connection being served by the handler under test.</summary>
    private sealed class Session(
        DuplexStream client, ScopeRegistry registry, Task handling, CancellationTokenSource shutdown, FakeAgentLiveness liveness, FakeTimeProvider clock, EffectiveConfiguration configuration, AppliedConfigurations applied, AgentConnections connections)
    {
        public AgentConnections Connections { get; } = connections;

        public EffectiveConfiguration Configuration { get; } = configuration;

        public AppliedConfigurations Applied { get; } = applied;

        public FakeAgentLiveness Liveness { get; } = liveness;

        public FakeTimeProvider Clock { get; } = clock;

        public ScopeRegistry Registry { get; } = registry;

        public Task Handling { get; } = handling;

        public Task SendAsync(Message message) =>
            FrameCodec.WriteAsync(client, message, ct: TestContext.Current.CancellationToken).AsTask();

        public Task SendRawAsync(byte[] bytes) =>
            client.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();

        /// <summary>Reads the next reply; null when the handler closed the connection. Fails instead of hanging.</summary>
        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            return await FrameCodec.ReadAsync(client, ct: guard.Token);
        }

        /// <summary>Sends a valid hello and waits for the configuration, leaving the session ready for state messages.</summary>
        public async Task<Session> HandshakeAsync(string token)
        {
            await SendAsync(HandlerFixture.Hello(token));
            (await ReceiveAsync()).ShouldBeOfType<Config>();
            return this;
        }

        public void Disconnect() => client.CompleteWriting();

        public void Shutdown() => shutdown.Cancel();
    }

    /// <summary>Authenticator that maps fixed tokens to scopes.</summary>
    private sealed class FakeAuthenticator(Dictionary<string, string> scopeByToken) : IAgentAuthenticator
    {
        public ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default) =>
            ValueTask.FromResult<AgentIdentity?>(
                hello.Token is not null && scopeByToken.TryGetValue(hello.Token, out var scope) ? new AgentIdentity(scope) : null);
    }
}
