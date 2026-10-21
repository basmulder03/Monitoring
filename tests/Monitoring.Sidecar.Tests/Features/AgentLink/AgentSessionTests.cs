using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Versioning;
using Monitoring.Sidecar.Features.AgentLink;
using Monitoring.Sidecar.Tests.Shared;
using Monitoring.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Sidecar.Tests.Features.AgentLink;

public class AgentSessionTests
{
    private static readonly InstanceDescription Orders = new("orders", "1.2.3");

    [Fact]
    public async Task RunAsync_registers_the_application_with_its_identity_and_labels()
    {
        // Arrange
        var fixture = new SessionFixture().WithInstance("orders-7d9f-abc12").WithLabel("team", "shop");
        var agent = fixture.Start();

        // Act
        var hello = await agent.ReceiveAsync();

        // Assert
        var message = hello.ShouldBeOfType<Hello>();
        message.ProtocolVersion.ShouldBe(ProtocolVersion.Current);
        message.InstanceId.ShouldBe("orders-7d9f-abc12");
        message.ServiceName.ShouldBe("orders");
        message.Version.ShouldBe("1.2.3");
        message.Token.ShouldBeNull();
        message.Labels["team"].ShouldBe("shop");
    }

    [Fact]
    public async Task RunAsync_sends_a_heartbeat_with_the_health_of_the_application_every_interval_the_agent_asked_for()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync(heartbeatSeconds: 5);
        fixture.Probe.Running("orders", "1.2.3", HealthState.Degraded);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var first = await agent.ReceiveAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var second = await agent.ReceiveAsync();

        // Assert
        first.ShouldBe(new Heartbeat(1, HealthState.Degraded));
        second.ShouldBe(new Heartbeat(2, HealthState.Degraded));
    }

    [Fact]
    public async Task RunAsync_reports_an_application_that_became_unhealthy_in_the_next_heartbeat()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();
        fixture.Probe.Running("orders", "1.2.3", HealthState.Unhealthy);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var heartbeat = await agent.ReceiveAsync();

        // Assert
        heartbeat.ShouldBe(new Heartbeat(1, HealthState.Unhealthy));
    }

    [Fact]
    public async Task RunAsync_follows_a_new_heartbeat_interval_sent_while_running()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync(heartbeatSeconds: 3600);

        // Act
        await agent.SendAsync(new Config("v2", [], HeartbeatSeconds: 2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var message = await agent.ReceiveAsync();

        // Assert
        message.ShouldBeOfType<Heartbeat>();
    }

    [Fact]
    public async Task RunAsync_answers_an_info_pull_with_the_description_of_the_application()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        await agent.SendAsync(new Pull(41, PullKind.Info, 0));
        var reply = await agent.ReceiveAsync();

        // Assert
        var result = reply.ShouldBeOfType<PullResult>();
        result.RequestId.ShouldBe(41);
        ReportPayload.ReadDescription(result.PayloadJson)!.Version.ShouldBe("1.2.3");
    }

    [Fact]
    public async Task RunAsync_answers_a_health_pull_with_the_latest_health_report()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();
        fixture.Probe.Set(Orders, new HealthReport(HealthState.Degraded, [new HealthCheckResult("cache", HealthState.Degraded, "cold")]));

        // Act
        await agent.SendAsync(new Pull(42, PullKind.Health, 0));
        var reply = await agent.ReceiveAsync();

        // Assert
        var report = ReportPayload.ReadHealth(reply.ShouldBeOfType<PullResult>().PayloadJson)!;
        report.Health.ShouldBe(HealthState.Degraded);
        report.Checks.Single().Name.ShouldBe("cache");
    }

    [Fact]
    public async Task RunAsync_answers_a_metrics_pull_with_what_the_application_reports()
    {
        // Arrange
        var fixture = new SessionFixture();
        fixture.Link.Metrics = new MetricsReport([new MetricSeries("orders.placed", new Dictionary<string, string>(), 4)]);
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        await agent.SendAsync(new Pull(44, PullKind.Metrics, 0));
        var reply = await agent.ReceiveAsync();

        // Assert
        var result = reply.ShouldBeOfType<PullResult>();
        result.RequestId.ShouldBe(44);
        ReportPayload.ReadMetrics(result.PayloadJson)!.Series.Single().Value.ShouldBe(4);
    }

    [Fact]
    public async Task RunAsync_answers_a_metrics_pull_with_nothing_when_the_application_cannot_be_reached()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        await agent.SendAsync(new Pull(45, PullKind.Metrics, 0));
        var reply = await agent.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<PullResult>().PayloadJson.ShouldBeNull();
    }

    [Theory]
    [InlineData(PullKind.Spans)]
    [InlineData(PullKind.Edges)]
    public async Task RunAsync_answers_kinds_of_data_it_does_not_provide_yet_with_nothing(PullKind kind)
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        await agent.SendAsync(new Pull(43, kind, 9));
        var reply = await agent.ReceiveAsync();

        // Assert
        var result = reply.ShouldBeOfType<PullResult>();
        result.RequestId.ShouldBe(43);
        result.PayloadJson.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_ends_to_register_again_when_the_application_introduces_itself_differently()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();
        fixture.Probe.Running("orders", "1.3.0");

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Reregister, Established: true));
    }

    [Fact]
    public async Task RunAsync_ends_as_disconnected_when_the_agent_closes_the_connection()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        agent.Disconnect();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Disconnected, Established: true));
    }

    [Fact]
    public async Task RunAsync_ends_as_disconnected_after_a_malformed_frame()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        await agent.SendRawAsync([0xFF, 0xFF, 0xFF, 0xFF]);
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Disconnected, Established: true));
    }

    [Fact]
    public async Task RunAsync_says_goodbye_and_ends_as_stopped_on_shutdown()
    {
        // Arrange
        var fixture = new SessionFixture().WithInstance("orders-1");
        var agent = fixture.Start();
        await agent.AcceptAsync();

        // Act
        fixture.Shutdown();
        var goodbye = await agent.ReceiveAsync();
        var result = await fixture.Result;

        // Assert
        goodbye.ShouldBe(new Goodbye("orders-1"));
        result.ShouldBe(new SessionResult(SessionEnd.Stopped, Established: true));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_agent_closes_instead_of_answering()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.ReceiveAsync();

        // Act
        agent.Disconnect();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_agent_answers_with_anything_but_a_configuration()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.ReceiveAsync();

        // Act
        await agent.SendAsync(new Ack(1));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_agent_does_not_answer_in_time()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.ReceiveAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_stops_without_a_goodbye_when_shut_down_before_the_agent_accepted_it()
    {
        // Arrange
        var fixture = new SessionFixture();
        var agent = fixture.Start();
        await agent.ReceiveAsync();

        // Act
        fixture.Shutdown();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Stopped, Established: false));
    }

    /// <summary>Builds a session over an in-memory connection and returns the agent's end of it.</summary>
    private sealed class SessionFixture
    {
        private readonly AgentLinkOptions _options = new() { AgentAddress = "agent:9441", InstanceId = "orders-1" };
        private readonly CancellationTokenSource _shutdown = new();

        public SessionFixture()
        {
            Probe.Running("orders", "1.2.3");
        }

        public FakeTimeProvider Clock { get; } = new();

        public FakeProbeResults Probe { get; } = new();

        public FakeLibraryLink Link { get; } = new();

        public Task<SessionResult> Result { get; private set; } = Task.FromException<SessionResult>(new InvalidOperationException("Not started."));

        public SessionFixture WithInstance(string id)
        {
            _options.InstanceId = id;
            return this;
        }

        public SessionFixture WithLabel(string key, string value)
        {
            _options.Labels[key] = value;
            return this;
        }

        public void Shutdown() => _shutdown.Cancel();

        public FakeAgent Start()
        {
            var session = new AgentSession(Probe, Link, _options, Clock, NullLogger<AgentSession>.Instance);
            var (server, client) = DuplexStream.CreatePair();
            Result = RunAsync(session, server);
            return new FakeAgent(client);
        }

        private async Task<SessionResult> RunAsync(AgentSession session, DuplexStream server)
        {
            try
            {
                return await session.RunAsync(server, Probe.Current.Description!, _shutdown.Token);
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    /// <summary>The agent's end of the connection, played by the test.</summary>
    private sealed class FakeAgent(DuplexStream connection)
    {
        public Task SendAsync(Message message) =>
            FrameCodec.WriteAsync(connection, message, ct: TestContext.Current.CancellationToken).AsTask();

        public Task SendRawAsync(byte[] bytes) =>
            connection.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();

        /// <summary>Reads the next message from the sidecar; null when it closed the connection. Fails instead of hanging.</summary>
        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            return await FrameCodec.ReadAsync(connection, ct: guard.Token);
        }

        /// <summary>Plays an agent that accepts the sidecar: reads the hello and answers with a configuration.</summary>
        public async Task AcceptAsync(int heartbeatSeconds = 5)
        {
            (await ReceiveAsync()).ShouldBeOfType<Hello>();
            await SendAsync(new Config("v1", [], HeartbeatSeconds: heartbeatSeconds));

            // The session starts its heartbeat clock after it received the configuration; give it a moment.
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        public void Disconnect() => connection.CompleteWriting();
    }
}
