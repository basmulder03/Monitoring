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
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.AgentLink;

/// <summary>Component tests: the real listener, handler, authenticator and registry over loopback TCP.</summary>
public class AgentListenerTests
{
    private const string Token = "secret";
    private const string Scope = "shop";

    [Fact]
    public async Task Start_binds_to_a_free_port_when_the_port_is_zero()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();

        // Act
        var endpoint = fixture.Start();

        // Assert
        endpoint.Port.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task LocalEndPoint_is_null_until_started_and_then_the_bound_endpoint()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        var before = fixture.Listener.LocalEndPoint;

        // Act
        var started = fixture.Start();

        // Assert
        before.ShouldBeNull();
        fixture.Listener.LocalEndPoint.ShouldBe(started);
    }

    [Fact]
    public async Task Start_rejects_a_second_call()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        fixture.Start();

        // Act
        var startAgain = () => fixture.Listener.Start();

        // Assert
        startAgain.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_requires_start_first()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();

        // Act
        var run = () => fixture.Listener.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        await run.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_agent_can_connect_authenticate_and_have_its_snapshot_applied()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        fixture.StartRunning();
        using var agent = await fixture.ConnectAsync();

        // Act
        await agent.SendAsync(new Hello(1, "agent-1", "agent", null, new Dictionary<string, string>(), Token));
        var config = await agent.ReceiveAsync();
        await agent.SendAsync(new Snapshot(3, Scope, [new InstanceInfo("pod-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>())]));
        var ack = await agent.ReceiveAsync();

        // Assert
        config.ShouldBeOfType<Config>();
        ack.ShouldBe(new Ack(3));
        fixture.Registry.GetInstances(Scope).Select(i => i.Id).ShouldBe(["pod-1"]);
    }

    [Fact]
    public async Task An_agent_with_a_wrong_token_is_disconnected()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        fixture.StartRunning();
        using var agent = await fixture.ConnectAsync();

        // Act
        await agent.SendAsync(new Hello(1, "agent-1", "agent", null, new Dictionary<string, string>(), "wrong"));
        var reply = await agent.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
    }

    [Fact]
    public async Task A_broken_connection_does_not_stop_the_listener_from_serving_others()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        fixture.StartRunning();
        using var broken = await fixture.ConnectAsync();
        await broken.SendRawAsync([0xFF, 0xFF, 0xFF, 0xFF]);
        (await broken.ReceiveAsync()).ShouldBeNull();

        // Act
        using var healthy = await fixture.ConnectAsync();
        await healthy.SendAsync(new Hello(1, "agent-2", "agent", null, new Dictionary<string, string>(), Token));
        var reply = await healthy.ReceiveAsync();

        // Assert
        reply.ShouldBeOfType<Config>();
    }

    [Fact]
    public async Task RunAsync_ends_connections_and_frees_the_port_when_cancelled()
    {
        // Arrange
        await using var fixture = new ListenerFixture().Build();
        var endpoint = fixture.StartRunning();
        using var agent = await fixture.ConnectAsync();
        await agent.SendAsync(new Hello(1, "agent-1", "agent", null, new Dictionary<string, string>(), Token));
        await agent.ReceiveAsync();

        // Act
        await fixture.StopAsync();
        var reply = await agent.ReceiveAsync();

        // Assert
        reply.ShouldBeNull();
        var reconnect = async () => await new TcpClient().ConnectAsync(endpoint, TestContext.Current.CancellationToken);
        await reconnect.ShouldThrowAsync<SocketException>();
    }

    /// <summary>Builds the controller pieces needed by an agent connection, listening on a free loopback port.</summary>
    private sealed class ListenerFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private ServiceProvider? _provider;
        private Task? _running;
        private IPEndPoint? _endpoint;

        public IAgentListener Listener => _provider!.GetRequiredService<IAgentListener>();

        public IScopeRegistry Registry => _provider!.GetRequiredService<IScopeRegistry>();

        public ListenerFixture Build()
        {
            _provider = new ServiceCollection()
                .AddRegistry()
                .AddLiveness()
                .AddConfiguration()
                .AddSecurity(o => o.AgentTokens.Add(new AgentTokenEntry(Scope, AgentTokenHash.Compute(Token))))
                .AddAgentLink(o =>
                {
                    o.ListenAddress = "127.0.0.1";
                    o.Port = 0;
                })
                .BuildServiceProvider();
            return this;
        }

        public IPEndPoint Start() => _endpoint = Listener.Start();

        /// <summary>Starts listening and serving; returns the bound endpoint.</summary>
        public IPEndPoint StartRunning()
        {
            var endpoint = Start();
            _running = Listener.RunAsync(_stop.Token);
            return endpoint;
        }

        public async Task<AgentClient> ConnectAsync()
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(_endpoint!, TestContext.Current.CancellationToken);
            return new AgentClient(tcp);
        }

        public async Task StopAsync()
        {
            await _stop.CancelAsync();
            if (_running is not null)
            {
                await _running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _provider?.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>An agent's TCP connection to the listener under test.</summary>
    private sealed class AgentClient(TcpClient tcp) : IDisposable
    {
        private readonly NetworkStream _stream = tcp.GetStream();

        public Task SendAsync(Message message) =>
            FrameCodec.WriteAsync(_stream, message, ct: TestContext.Current.CancellationToken).AsTask();

        public Task SendRawAsync(byte[] bytes) =>
            _stream.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();

        /// <summary>Reads the next reply; null when the controller closed the connection. Fails instead of hanging.</summary>
        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                return await FrameCodec.ReadAsync(_stream, ct: guard.Token);
            }
            catch (IOException)
            {
                // A reset instead of a clean close still means the controller ended the connection.
                return null;
            }
        }

        public void Dispose() => tcp.Dispose();
    }
}
