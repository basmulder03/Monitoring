using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Agent.Features.Configuration;
using Plover.Agent.Features.ControllerLink;
using Plover.Agent.Features.MetricsHistory;
using Plover.Agent.Features.ScopeState;
using Plover.Agent.Features.SelfMonitoring;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Protocol.Versioning;
using Plover.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.ControllerLink;

public class ControllerSessionTests
{
    [Fact]
    public async Task RunAsync_introduces_the_agent_with_its_identity_labels_token_and_scope()
    {
        // Arrange
        var controller = new SessionFixture()
            .WithAgent("agent-7", "1.2.3", token: "secret")
            .WithLabels(("env", "prod"))
            .Start();

        // Act
        var hello = await controller.ReceiveAsync();

        // Assert
        var expected = new Hello(ProtocolVersion.Current, "agent-7", "plover-agent", "1.2.3", new Dictionary<string, string> { ["env"] = "prod" }, "secret", "shop");
        hello.ShouldBeOfType<Hello>().ShouldBeEquivalentTo(expected);
    }

    [Fact]
    public async Task RunAsync_reports_the_configuration_the_controller_sent()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();

        // Act
        await controller.AcceptAsync(new Config("v1", ["a:9440", "b:9440"]));

        // Assert
        fixture.Configs.Single().ControllerReplicas.ShouldBe(["a:9440", "b:9440"]);
    }

    [Fact]
    public async Task RunAsync_sends_the_state_of_the_scope_as_the_first_snapshot()
    {
        // Arrange
        var fixture = new SessionFixture().WithInstances("pod-2", "pod-1");
        var controller = fixture.Start();

        // Act
        var snapshot = await controller.AcceptAsync();

        // Assert
        snapshot.ScopeName.ShouldBe("shop");
        snapshot.Instances.Select(i => i.Id).ShouldBe(["pod-1", "pod-2"]);
    }

    [Fact]
    public async Task RunAsync_sends_each_published_delta()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        fixture.Store.Upsert(SessionFixture.Instance("pod-1"));
        fixture.Store.Publish();
        var message = await controller.ReceiveAsync();

        // Assert
        var delta = message.ShouldBeOfType<Delta>();
        delta.Sequence.ShouldBe(1);
        delta.Upserted.Single().Id.ShouldBe("pod-1");
    }

    [Fact]
    public async Task RunAsync_sends_deltas_in_order()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        fixture.Store.Upsert(SessionFixture.Instance("a"));
        fixture.Store.Publish();
        fixture.Store.Upsert(SessionFixture.Instance("b"));
        fixture.Store.Publish();
        var first = await controller.ReceiveAsync();
        var second = await controller.ReceiveAsync();

        // Assert
        ((Delta)first!).Sequence.ShouldBe(1);
        ((Delta)second!).Sequence.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_sends_a_heartbeat_every_interval_the_controller_asked_for()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 5));

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var first = await controller.ReceiveAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var second = await controller.ReceiveAsync();

        // Assert
        first.ShouldBe(new Heartbeat(1, HealthState.Healthy, "built-in"));
        second.ShouldBe(new Heartbeat(2, HealthState.Healthy, "built-in"));
    }

    [Fact]
    public async Task RunAsync_presents_the_token_from_the_file_and_reads_the_file_again_for_every_session()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"token-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(file, "first-token\n", TestContext.Current.CancellationToken);
            var firstHello = (await new SessionFixture().WithTokenFile(file).Start().ReceiveAsync()).ShouldBeOfType<Hello>();

            // Act: Kubernetes has rotated the token in the meantime
            await File.WriteAllTextAsync(file, "second-token", TestContext.Current.CancellationToken);
            var secondHello = (await new SessionFixture().WithTokenFile(file).Start().ReceiveAsync()).ShouldBeOfType<Hello>();

            // Assert
            firstHello.Token.ShouldBe("first-token");
            secondHello.Token.ShouldBe("second-token");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task RunAsync_keeps_track_of_whether_it_is_connected_to_the_replica_and_what_the_replica_acknowledged()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 3600));
        var connected = fixture.Telemetry.Read()[(ReplicaMetrics.Connected, "c1:9440")];

        // Act
        await controller.SendAsync(new Ack(5));
        await Eventually(() => fixture.Telemetry.Read().GetValueOrDefault((ReplicaMetrics.AckedSequence, "c1:9440")) == 5);
        controller.Disconnect();
        await fixture.Result;

        // Assert
        connected.ShouldBe(1);
        fixture.Telemetry.Read()[(ReplicaMetrics.Connected, "c1:9440")].ShouldBe(0);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(5, guard.Token);
        }
    }

    [Fact]
    public async Task RunAsync_answers_a_request_for_the_history_of_an_instance_from_what_it_kept()
    {
        // Arrange
        var fixture = new SessionFixture();
        fixture.History.Record("orders-1", [new MetricSeries("orders.placed", new Dictionary<string, string>(), 4)]);
        fixture.History.Record("orders-1", [new MetricSeries("orders.placed", new Dictionary<string, string>(), 6)]);
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 3600));

        // Act
        await controller.SendAsync(new HistoryRequest(9, "orders-1"));
        var reply = await controller.ReceiveAsync();

        // Assert
        var result = reply.ShouldBeOfType<HistoryResult>();
        result.RequestId.ShouldBe(9);
        var series = ReportPayload.ReadHistory(result.PayloadJson)!.Series.Single();
        series.Values.ShouldBe([4, 6]);
    }

    [Fact]
    public async Task RunAsync_answers_a_request_for_an_instance_it_keeps_nothing_for_with_nothing()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 3600));

        // Act
        await controller.SendAsync(new HistoryRequest(10, "unknown"));
        var reply = await controller.ReceiveAsync();

        // Assert
        reply.ShouldBe(new HistoryResult(10, null));
    }

    [Fact]
    public async Task RunAsync_says_in_each_heartbeat_which_configuration_the_agent_uses()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 5));
        fixture.Configuration.Apply(new Config("v9", [], PullSeconds: 20));

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var heartbeat = await controller.ReceiveAsync();

        // Assert
        heartbeat.ShouldBeOfType<Heartbeat>().ConfigVersion.ShouldBe("v9");
    }

    [Fact]
    public async Task RunAsync_sends_a_reconciliation_snapshot_at_the_current_sequence_every_snapshot_interval()
    {
        // Arrange
        var fixture = new SessionFixture().WithSnapshotInterval(TimeSpan.FromMinutes(5));
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v", [], HeartbeatSeconds: 3600));
        fixture.Store.Upsert(SessionFixture.Instance("pod-1"));
        fixture.Store.Publish();
        (await controller.ReceiveAsync()).ShouldBeOfType<Delta>();

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        var message = await controller.ReceiveAsync();

        // Assert
        var snapshot = message.ShouldBeOfType<Snapshot>();
        snapshot.Sequence.ShouldBe(1);
        snapshot.Instances.Single().Id.ShouldBe("pod-1");
    }

    [Fact]
    public async Task RunAsync_adopts_a_new_heartbeat_interval_sent_while_running()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));

        // Act
        await controller.SendAsync(new Config("v2", [], HeartbeatSeconds: 2));
        await fixture.WaitForConfigCountAsync(2);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var message = await controller.ReceiveAsync();

        // Assert
        message.ShouldBeOfType<Heartbeat>();
    }

    [Fact]
    public async Task RunAsync_reports_every_configuration_change()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync(new Config("v1", ["a:9440"]));

        // Act
        await controller.SendAsync(new Config("v2", ["a:9440", "b:9440"]));
        await fixture.WaitForConfigCountAsync(2);

        // Assert
        fixture.Configs.Select(c => c.Version).ShouldBe(["v1", "v2"]);
    }

    [Fact]
    public async Task RunAsync_ends_with_a_resync_when_the_controller_asks_for_a_snapshot()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        await controller.SendAsync(new Ack(0, SnapshotRequired: true));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.ResyncRequested, Established: true));
    }

    [Fact]
    public async Task RunAsync_ends_with_a_resync_when_the_store_cut_the_connection_off()
    {
        // Arrange
        var channel = Channel.CreateUnbounded<Delta>();
        var fixture = new SessionFixture().WithStore(new CutOffStore(channel));
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        channel.Writer.TryComplete(new InvalidOperationException("fell behind"));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.ResyncRequested, Established: true));
    }

    [Fact]
    public async Task RunAsync_ends_as_disconnected_when_the_controller_closes_the_connection()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        controller.Disconnect();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Disconnected, Established: true));
    }

    [Fact]
    public async Task RunAsync_ends_as_disconnected_after_a_malformed_frame()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        await controller.SendRawAsync([0xFF, 0xFF, 0xFF, 0xFF]);
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Disconnected, Established: true));
    }

    [Fact]
    public async Task RunAsync_says_goodbye_and_ends_as_stopped_on_shutdown()
    {
        // Arrange
        var fixture = new SessionFixture().WithAgent("agent-7", null, "secret");
        var controller = fixture.Start();
        await controller.AcceptAsync();

        // Act
        fixture.Shutdown();
        var goodbye = await controller.ReceiveAsync();
        var result = await fixture.Result;

        // Assert
        goodbye.ShouldBe(new Goodbye("agent-7"));
        result.ShouldBe(new SessionResult(SessionEnd.Stopped, Established: true));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_controller_closes_instead_of_answering()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.ReceiveAsync();

        // Act
        controller.Disconnect();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_controller_answers_with_anything_but_a_configuration()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.ReceiveAsync();

        // Act
        await controller.SendAsync(new Ack(1));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_is_refused_when_the_controller_does_not_answer_in_time()
    {
        // Arrange
        var fixture = new SessionFixture().WithReplyTimeout(TimeSpan.FromSeconds(10));
        var controller = fixture.Start();
        await controller.ReceiveAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Refused, Established: false));
    }

    [Fact]
    public async Task RunAsync_stops_without_a_goodbye_when_shut_down_before_the_controller_accepted_it()
    {
        // Arrange
        var fixture = new SessionFixture();
        var controller = fixture.Start();
        await controller.ReceiveAsync();

        // Act
        fixture.Shutdown();
        var result = await fixture.Result;

        // Assert
        result.ShouldBe(new SessionResult(SessionEnd.Stopped, Established: false));
    }

    /// <summary>Builds a session over an in-memory connection and returns the controller's end of it.</summary>
    private sealed class SessionFixture
    {
        private readonly ControllerLinkOptions _options = new() { Token = "token", AgentId = "agent-1" };
        private readonly CancellationTokenSource _shutdown = new();
        private readonly List<string> _instanceIds = [];
        private IScopeStateStore? _store;

        public FakeTimeProvider Clock { get; } = new();

        public AgentConfiguration Configuration { get; } = new(NullLogger<AgentConfiguration>.Instance);

        public List<Config> Configs { get; } = [];

        public IScopeStateStore Store => _store!;

        public IMetricsHistory History { get; } = new RingMetricsHistory(new MetricsHistoryOptions());

        public AgentTelemetry Telemetry { get; } = new();

        public Task<SessionResult> Result { get; private set; } = Task.FromException<SessionResult>(new InvalidOperationException("Not started."));

        public SessionFixture WithAgent(string id, string? version, string token)
        {
            _options.AgentId = id;
            _options.AgentVersion = version;
            _options.Token = token;
            return this;
        }

        public SessionFixture WithTokenFile(string path)
        {
            _options.TokenFile = path;
            return this;
        }

        public SessionFixture WithLabels(params (string Key, string Value)[] labels)
        {
            _options.Labels = labels.ToDictionary(l => l.Key, l => l.Value);
            return this;
        }

        public SessionFixture WithInstances(params string[] ids)
        {
            _instanceIds.AddRange(ids);
            return this;
        }

        public SessionFixture WithSnapshotInterval(TimeSpan interval)
        {
            _options.SnapshotInterval = interval;
            return this;
        }

        public SessionFixture WithReplyTimeout(TimeSpan timeout)
        {
            _options.ConfigReplyTimeout = timeout;
            return this;
        }

        public SessionFixture WithStore(IScopeStateStore store)
        {
            _store = store;
            return this;
        }

        public static InstanceInfo Instance(string id) =>
            new(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>());

        /// <summary>Starts the session; its handshake timer exists by the time this returns.</summary>
        public FakeController Start()
        {
            _store ??= new StoreBuilder().WithInstances(_instanceIds).Build();
            var session = new ControllerSession(_store, Configuration, _options, Clock, NullLogger<ControllerSession>.Instance, Telemetry, History);
            var (server, client) = DuplexStream.CreatePair();
            Result = RunAsync(session, server);
            return new FakeController(client);
        }

        public void Shutdown() => _shutdown.Cancel();

        /// <summary>Waits until the session has reported the given number of configurations.</summary>
        public async Task WaitForConfigCountAsync(int count)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                lock (Configs)
                {
                    if (Configs.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(5, guard.Token);
            }
        }

        private async Task<SessionResult> RunAsync(ControllerSession session, DuplexStream server)
        {
            try
            {
                return await session.RunAsync("c1:9440", server, config => { lock (Configs) { Configs.Add(config); } }, _shutdown.Token);
            }
            finally
            {
                server.Dispose();
            }
        }
    }

    /// <summary>Builds a real store holding the given instances, all published.</summary>
    private sealed class StoreBuilder
    {
        private readonly ScopeStateOptions _options = new() { ScopeName = "shop" };
        private IEnumerable<string> _ids = [];

        public StoreBuilder WithInstances(IEnumerable<string> ids)
        {
            _ids = ids;
            return this;
        }

        public IScopeStateStore Build()
        {
            var store = new ScopeStateStore(_options);
            foreach (var id in _ids)
            {
                store.Upsert(SessionFixture.Instance(id));
            }

            store.Publish();
            return store;
        }
    }

    /// <summary>The controller's end of the connection, played by the test.</summary>
    private sealed class FakeController(DuplexStream connection)
    {
        public Task SendAsync(Message message) =>
            FrameCodec.WriteAsync(connection, message, ct: TestContext.Current.CancellationToken).AsTask();

        public Task SendRawAsync(byte[] bytes) =>
            connection.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();

        /// <summary>Reads the next message from the agent; null when it closed the connection. Fails instead of hanging.</summary>
        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            return await FrameCodec.ReadAsync(connection, ct: guard.Token);
        }

        /// <summary>Plays a controller that accepts the agent: reads the hello, answers with a configuration and returns the first snapshot.</summary>
        public async Task<Snapshot> AcceptAsync(Config? config = null)
        {
            (await ReceiveAsync()).ShouldBeOfType<Hello>();
            await SendAsync(config ?? new Config("v1", []));
            return (await ReceiveAsync()).ShouldBeOfType<Snapshot>();
        }

        public void Disconnect() => connection.CompleteWriting();
    }

    /// <summary>A store whose delta stream the test controls, to simulate being cut off.</summary>
    private sealed class CutOffStore(Channel<Delta> channel) : IScopeStateStore
    {
        public string ScopeName => "shop";

        public void Upsert(InstanceInfo instance) => throw new NotSupportedException();

        public void Remove(string id) => throw new NotSupportedException();

        public void Report(string id, ApplicationReport report) => throw new NotSupportedException();

        public void LoseSidecar(string id) => throw new NotSupportedException();

        public InstanceInfo? Find(string id) => null;

        public void ForgetReport(string id) => throw new NotSupportedException();

        public void Publish() => throw new NotSupportedException();

        public Snapshot CreateSnapshot() => new(0, ScopeName, []);

        public StateSubscription Subscribe() => new(CreateSnapshot(), channel.Reader, () => channel.Writer.TryComplete());
    }
}
