using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SidecarConnections;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Versioning;
using Monitoring.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.SidecarConnections;

public class SidecarConnectionHandlerTests
{
    [Fact]
    public async Task HandleAsync_answers_a_hello_with_the_heartbeat_interval()
    {
        // Arrange
        var session = new HandlerFixture().WithHeartbeat(TimeSpan.FromSeconds(7)).Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello("pod-1"));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Config>().HeartbeatSeconds.ShouldBe(7);
    }

    [Fact]
    public async Task HandleAsync_pulls_the_health_of_the_application_after_the_hello()
    {
        // Arrange
        var session = new HandlerFixture().Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello("pod-1"));
        await session.ReceiveAsync();
        var pull = await session.ReceiveAsync();

        // Assert
        pull.ShouldBeOfType<Pull>().Kind.ShouldBe(PullKind.Health);
    }

    [Theory]
    [InlineData(ProtocolVersion.Current - 2)]
    [InlineData(ProtocolVersion.Current + 2)]
    public async Task HandleAsync_closes_the_connection_for_an_unsupported_protocol_version(int version)
    {
        // Arrange
        var session = new HandlerFixture().Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello("pod-1", version));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "orders")]
    [InlineData("pod-1", "")]
    public async Task HandleAsync_closes_the_connection_when_the_hello_names_no_instance_or_service(string instance, string service)
    {
        // Arrange
        var session = new HandlerFixture().Build();

        // Act
        await session.SendAsync(HandlerFixture.Hello(instance, service: service));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_when_the_first_message_is_not_a_hello()
    {
        // Arrange
        var session = new HandlerFixture().Build();

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
        var fixture = new HandlerFixture().WithHelloTimeout(TimeSpan.FromSeconds(10));
        var session = fixture.Build();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var reply = await session.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_reports_the_health_the_application_answers_with()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1", version: "1.2");
        var checks = new[] { new HealthCheckResult("database", HealthState.Degraded, "slow") };

        // Act
        await session.AnswerHealthAsync(HealthState.Degraded, checks);
        await session.SyncAsync(HealthState.Degraded);

        // Assert
        var instance = fixture.Store.CreateSnapshot().Instances.Single();
        instance.Health.ShouldBe(HealthState.Degraded);
        instance.ServiceName.ShouldBe("orders");
        instance.Version.ShouldBe("1.2");
        instance.Checks.ShouldBe(checks);
    }

    [Fact]
    public async Task HandleAsync_publishes_what_the_application_reports()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        using var subscription = fixture.Store.Subscribe();
        var session = await fixture.Build().RegisterAsync("pod-1");

        // Act
        await session.AnswerHealthAsync(HealthState.Unhealthy);
        await session.SyncAsync(HealthState.Unhealthy);

        // Assert
        subscription.Deltas.TryRead(out var delta).ShouldBeTrue();
        delta!.Upserted.Single().Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task HandleAsync_pulls_again_when_a_heartbeat_shows_a_different_health()
    {
        // Arrange
        var session = await new HandlerFixture().WithInstance("pod-1").Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Unhealthy));
        var pull = await session.ReceiveAsync();

        // Assert
        pull.ShouldBeOfType<Pull>().Kind.ShouldBe(PullKind.Health);
    }

    [Fact]
    public async Task HandleAsync_does_not_pull_again_while_an_answer_is_awaited()
    {
        // Arrange
        var session = await new HandlerFixture().WithInstance("pod-1").Build().RegisterAsync("pod-1");

        // Act: the first pull is still unanswered
        await session.SendAsync(new Heartbeat(1, HealthState.Unhealthy));
        await session.SendAsync(new Goodbye("pod-1"));
        var rest = await session.ReadUntilClosedAsync();

        // Assert
        rest.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_pulls_again_after_the_pull_interval_even_when_nothing_changed()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1").WithHeartbeat(TimeSpan.FromSeconds(10)).WithHealthPullInterval(TimeSpan.FromSeconds(15));
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);
        fixture.Clock.Advance(TimeSpan.FromSeconds(15));

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        var pull = await session.ReceiveAsync();

        // Assert
        pull.ShouldBeOfType<Pull>().Kind.ShouldBe(PullKind.Health);
    }

    [Fact]
    public async Task HandleAsync_pulls_the_metrics_once_the_application_has_reported_its_health()
    {
        // Arrange
        var session = await new HandlerFixture().WithInstance("pod-1").Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        var pull = await session.ReceiveAsync();

        // Assert
        pull.ShouldBeOfType<Pull>().Kind.ShouldBe(PullKind.Metrics);
    }

    [Fact]
    public async Task HandleAsync_does_not_pull_metrics_before_the_application_has_reported_its_health()
    {
        // Arrange
        var session = await new HandlerFixture().WithInstance("pod-1").Build().RegisterAsync("pod-1");

        // Act
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        await session.SendAsync(new Goodbye("pod-1"));
        var rest = await session.ReadUntilClosedAsync();

        // Assert
        rest.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandleAsync_reports_the_metrics_the_application_answers_with()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        var metricsPull = (await session.ReceiveAsync()).ShouldBeOfType<Pull>();
        var metrics = new MetricsReport([new MetricSeries("orders.placed", new Dictionary<string, string>(), 7)]);

        // Act
        await session.SendAsync(new PullResult(metricsPull.RequestId, PullKind.Metrics, 0, ReportPayload.Write(metrics)));
        await session.SyncAsync(HealthState.Healthy);

        // Assert
        var instance = fixture.Store.CreateSnapshot().Instances.Single();
        instance.Metrics!.Single().Name.ShouldBe("orders.placed");
        instance.Metrics!.Single().Value.ShouldBe(7);
        instance.Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public async Task HandleAsync_keeps_the_metrics_when_the_health_is_reported_again()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);
        await session.SendAsync(new Heartbeat(1, HealthState.Healthy));
        var metricsPull = (await session.ReceiveAsync()).ShouldBeOfType<Pull>();
        await session.SendAsync(new PullResult(metricsPull.RequestId, PullKind.Metrics, 0,
            ReportPayload.Write(new MetricsReport([new MetricSeries("orders.placed", new Dictionary<string, string>(), 7)]))));
        await session.SyncAsync(HealthState.Healthy);

        // Act
        await session.AnswerHealthAsync(HealthState.Degraded);
        await session.SyncAsync(HealthState.Degraded);

        // Assert
        var instance = fixture.Store.CreateSnapshot().Instances.Single();
        instance.Health.ShouldBe(HealthState.Degraded);
        instance.Metrics!.Single().Value.ShouldBe(7);
    }

    [Fact]
    public async Task HandleAsync_ignores_an_answer_that_is_not_readable()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");

        // Act
        await session.SendAsync(new PullResult(session.LastPullId, PullKind.Health, 0, "not json"));
        await session.SyncAsync(HealthState.Unknown);

        // Assert
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public async Task HandleAsync_marks_the_instance_unhealthy_when_the_connection_drops()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);

        // Act
        session.Disconnect();
        await session.Handling;

        // Assert
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task HandleAsync_closes_the_connection_after_the_missed_heartbeats()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1").WithHeartbeat(TimeSpan.FromSeconds(5), missed: 3);
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Healthy);
        await session.SyncAsync(HealthState.Healthy);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(15));
        await session.Handling;

        // Assert
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task HandleAsync_does_not_mark_the_instance_unhealthy_after_a_goodbye()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Degraded);

        // Act
        await session.SendAsync(new Goodbye("pod-1"));
        await session.Handling;

        // Assert: back to what the platform says
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public async Task HandleAsync_keeps_the_report_of_a_sidecar_that_reconnected_when_the_old_connection_ends()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var old = await fixture.Build().RegisterAsync("pod-1");
        var replacement = await fixture.Build().RegisterAsync("pod-1");
        await replacement.AnswerHealthAsync(HealthState.Degraded);
        await replacement.SyncAsync(HealthState.Degraded);

        // Act
        old.Disconnect();
        await old.Handling;

        // Assert
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Degraded);
    }

    [Fact]
    public async Task HandleAsync_leaves_the_state_alone_when_the_agent_shuts_down()
    {
        // Arrange
        var fixture = new HandlerFixture().WithInstance("pod-1");
        var session = await fixture.Build().RegisterAsync("pod-1");
        await session.AnswerHealthAsync(HealthState.Degraded);
        await session.SyncAsync(HealthState.Degraded);

        // Act
        session.Shutdown();
        await session.Handling;

        // Assert
        fixture.Store.CreateSnapshot().Instances.Single().Health.ShouldBe(HealthState.Degraded);
    }

    private sealed class HandlerFixture
    {
        private readonly SidecarConnectionsOptions _options = new();
        private readonly List<string> _instances = [];
        private SidecarConnectionHandler? _handler;

        public FakeTimeProvider Clock { get; } = new();

        public ScopeStateStore Store { get; } = new(new ScopeStateOptions { ScopeName = "shop" });

        public HandlerFixture WithHeartbeat(TimeSpan interval, int missed = 3)
        {
            _options.HeartbeatInterval = interval;
            _options.MissedHeartbeats = missed;
            return this;
        }

        public HandlerFixture WithHelloTimeout(TimeSpan timeout)
        {
            _options.HelloTimeout = timeout;
            return this;
        }

        public HandlerFixture WithHealthPullInterval(TimeSpan interval)
        {
            _options.HealthPullInterval = interval;
            return this;
        }

        /// <summary>An instance the platform knows about, so reports about it have something to add to.</summary>
        public HandlerFixture WithInstance(string id)
        {
            Store.Upsert(new InstanceInfo(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));
            return this;
        }

        public static Hello Hello(string instance, int version = ProtocolVersion.Current, string service = "orders", string? appVersion = null) =>
            new(version, instance, service, appVersion, new Dictionary<string, string>());

        /// <summary>Starts the handler on the server end of a new connection and returns the sidecar's end. May be called again for another connection.</summary>
        public Session Build()
        {
            // One handler for all connections, as in the agent, so a reconnect can be told from the old connection.
            _handler ??= new SidecarConnectionHandler(Store, _options, Clock, NullLogger<SidecarConnectionHandler>.Instance);
            var (server, client) = DuplexStream.CreatePair();
            var shutdown = new CancellationTokenSource();
            var handling = ServeAsync(_handler, server, shutdown.Token);
            return new Session(client, handling, shutdown);
        }

        /// <summary>Closes the server end when handling ends, as the listener does when it disposes the socket.</summary>
        private static async Task ServeAsync(SidecarConnectionHandler handler, DuplexStream server, CancellationToken ct)
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

    /// <summary>The sidecar's end of a connection being served by the handler under test.</summary>
    private sealed class Session(DuplexStream client, Task handling, CancellationTokenSource shutdown)
    {
        public Task Handling { get; } = handling;

        public long LastPullId { get; private set; }

        public Task SendAsync(Message message) =>
            FrameCodec.WriteAsync(client, message, ct: TestContext.Current.CancellationToken).AsTask();

        /// <summary>Reads the next message; null when the handler closed the connection. Fails instead of hanging.</summary>
        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            return await FrameCodec.ReadAsync(client, ct: guard.Token);
        }

        /// <summary>Sends a hello and reads the configuration and the first pull, leaving the session ready for heartbeats and answers.</summary>
        public async Task<Session> RegisterAsync(string instance, string? version = null)
        {
            await SendAsync(HandlerFixture.Hello(instance, appVersion: version));
            (await ReceiveAsync()).ShouldBeOfType<Config>();
            LastPullId = (await ReceiveAsync()).ShouldBeOfType<Pull>().RequestId;
            return this;
        }

        /// <summary>Answers the outstanding health pull.</summary>
        public Task AnswerHealthAsync(HealthState health, params HealthCheckResult[] checks) =>
            SendAsync(new PullResult(LastPullId, PullKind.Health, 0, ReportPayload.Write(new HealthReport(health, checks))));

        /// <summary>
        /// Waits until the handler has dealt with everything sent so far. A heartbeat whose health differs from the last reported one
        /// is always answered with a pull, and the handler writes it only after the earlier messages, so that pull marks the point.
        /// </summary>
        /// <param name="reported">The health the application last reported; the marker heartbeat shows another one.</param>
        public async Task SyncAsync(HealthState reported)
        {
            await SendAsync(new Heartbeat(long.MaxValue, reported == HealthState.Unknown ? HealthState.Healthy : HealthState.Unknown));
            var marker = (await ReceiveAsync()).ShouldBeOfType<Pull>();
            LastPullId = marker.RequestId;
        }

        /// <summary>Reads until the handler closes the connection, returning what it still sent.</summary>
        public async Task<IReadOnlyList<Message>> ReadUntilClosedAsync()
        {
            var rest = new List<Message>();
            while (await ReceiveAsync() is { } message)
            {
                rest.Add(message);
            }

            return rest;
        }

        public void Disconnect() => client.CompleteWriting();

        public void Shutdown() => shutdown.Cancel();
    }
}
