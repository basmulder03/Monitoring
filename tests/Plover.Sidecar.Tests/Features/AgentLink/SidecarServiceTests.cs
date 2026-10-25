using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Sidecar.Features.AgentLink;
using Plover.Sidecar.Tests.Shared;
using Plover.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Sidecar.Tests.Features.AgentLink;

public class SidecarServiceTests
{
    [Fact]
    public async Task Service_does_not_connect_before_the_application_has_introduced_itself()
    {
        // Arrange
        var fixture = new ServiceFixture();

        // Act
        await fixture.StartAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        fixture.ConnectAttempts.ShouldBe(0);
    }

    [Fact]
    public async Task Service_registers_the_application_once_it_has_introduced_itself()
    {
        // Arrange
        var fixture = new ServiceFixture();
        await fixture.StartAsync();

        // Act
        fixture.Probe.Running("orders", "1.2.3");
        var hello = await fixture.AcceptAsync();

        // Assert
        hello.ServiceName.ShouldBe("orders");
        hello.Version.ShouldBe("1.2.3");
        hello.InstanceId.ShouldBe("orders-1");
    }

    [Fact]
    public async Task Service_retries_with_a_growing_wait_while_the_agent_cannot_be_reached()
    {
        // Arrange
        var fixture = new ServiceFixture().WithConnectFailing(times: 2);
        fixture.Probe.Running("orders");
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.ConnectAttempts == 1);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act: the first wait is 1 s, the second 2 s (full jitter)
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitUntilAsync(() => fixture.ConnectAttempts == 2);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var hello = await fixture.AcceptAsync();

        // Assert
        hello.ServiceName.ShouldBe("orders");
        fixture.ConnectAttempts.ShouldBe(3);
    }

    [Fact]
    public async Task Service_connects_again_after_the_agent_closed_the_connection()
    {
        // Arrange
        var fixture = new ServiceFixture();
        fixture.Probe.Running("orders");
        await fixture.StartAsync();
        var first = await fixture.AcceptAgentAsync();
        await first.CompleteHandshakeAsync();

        // Act
        first.Disconnect();
        await fixture.WaitUntilAsync(() => fixture.ConnectAttempts >= 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await fixture.AcceptAgentAsync();

        // Assert
        (await second.ReceiveAsync()).ShouldBeOfType<Hello>();
        fixture.ConnectAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task Service_registers_again_with_the_new_version_when_the_application_changed()
    {
        // Arrange
        var fixture = new ServiceFixture();
        fixture.Probe.Running("orders", "1.0.0");
        await fixture.StartAsync();
        var first = await fixture.AcceptAgentAsync();
        await first.CompleteHandshakeAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        fixture.Probe.Running("orders", "2.0.0");
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.ConnectAttempts >= 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await fixture.AcceptAgentAsync();
        var hello = (Hello)(await second.ReceiveAsync())!;

        // Assert
        hello.Version.ShouldBe("2.0.0");
    }

    [Fact]
    public async Task Service_says_goodbye_to_the_agent_when_it_stops()
    {
        // Arrange
        var fixture = new ServiceFixture();
        fixture.Probe.Running("orders");
        await fixture.StartAsync();
        var agent = await fixture.AcceptAgentAsync();
        await agent.CompleteHandshakeAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        var stopping = fixture.Service.StopAsync(TestContext.Current.CancellationToken);
        var goodbye = await agent.ReceiveAsync();
        await stopping;

        // Assert
        goodbye.ShouldBe(new Goodbye("orders-1"));
    }

    [Fact]
    public async Task Service_stops_without_throwing_while_it_waits_for_the_application()
    {
        // Arrange
        var fixture = new ServiceFixture();
        await fixture.StartAsync();

        // Act
        var stop = async () => await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Builds the service over a fake probe, a fake clock, a scripted connection and the real session; waits are the minimum, with full jitter.</summary>
    private sealed class ServiceFixture
    {
        private readonly AgentLinkOptions _options = new()
        {
            AgentAddress = "agent:9441",
            InstanceId = "orders-1",
            ReconnectMinDelay = TimeSpan.FromSeconds(1),
            ReconnectMaxDelay = TimeSpan.FromSeconds(30),
        };

        private readonly Queue<FakeAgent> _agents = new();
        private int _failuresLeft;
        private int _attempts;

        public ServiceFixture()
        {
            Service = new SidecarService(
                Probe,
                new AgentSession(Probe, new FakeLibraryLink(), _options, Clock, NullLogger<AgentSession>.Instance),
                ConnectAsync,
                _options,
                Clock,
                () => 1.0,
                NullLogger<SidecarService>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new();

        public FakeProbeResults Probe { get; } = new();

        public SidecarService Service { get; }

        public int ConnectAttempts => Volatile.Read(ref _attempts);

        public ServiceFixture WithConnectFailing(int times)
        {
            _failuresLeft = times;
            return this;
        }

        public Task StartAsync() => Service.StartAsync(TestContext.Current.CancellationToken);

        /// <summary>Waits for the sidecar to connect and returns the agent's end of that connection.</summary>
        public async Task<FakeAgent> AcceptAgentAsync()
        {
            await WaitUntilAsync(() =>
            {
                lock (_agents)
                {
                    return _agents.Count > 0;
                }
            });

            lock (_agents)
            {
                return _agents.Dequeue();
            }
        }

        /// <summary>Waits for the sidecar to connect and returns its registration.</summary>
        public async Task<Hello> AcceptAsync()
        {
            var agent = await AcceptAgentAsync();
            return (Hello)(await agent.ReceiveAsync())!;
        }

        public async Task WaitUntilAsync(Func<bool> condition)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                await Task.Delay(5, guard.Token);
            }
        }

        private ValueTask<Stream> ConnectAsync(string address, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw new IOException("agent unreachable");
            }

            var (server, client) = DuplexStream.CreatePair();
            lock (_agents)
            {
                _agents.Enqueue(new FakeAgent(client));
            }

            return ValueTask.FromResult<Stream>(server);
        }
    }

    /// <summary>The agent's end of a connection, played by the test.</summary>
    private sealed class FakeAgent(DuplexStream connection)
    {
        private bool _helloRead;

        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            var message = await FrameCodec.ReadAsync(connection, ct: guard.Token);
            _helloRead |= message is Hello;
            return message;
        }

        /// <summary>Reads the registration and answers with a configuration.</summary>
        public async Task CompleteHandshakeAsync()
        {
            (await ReceiveAsync()).ShouldBeOfType<Hello>();
            await FrameCodec.WriteAsync(connection, new Config("v1", [], HeartbeatSeconds: 5), ct: TestContext.Current.CancellationToken);
        }

        public void Disconnect() => connection.CompleteWriting();
    }
}
