using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.AgentToController;

/// <summary>The real agent talking to real controller replicas over loopback TCP, in one process.</summary>
public class AgentToControllerTests
{
    private const string Scope = "shop";
    private const string Token = "secret";

    [Fact]
    public async Task Instances_added_on_the_agent_appear_on_the_controller()
    {
        // Arrange
        await using var controller = new ControllerNode().Start();
        await using var agent = new AgentNode().ConnectingTo(controller).Start();

        // Act
        agent.Store.Upsert(AgentNode.Instance("pod-1"));
        agent.Store.Publish();

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds().ShouldBe(["pod-1"]));
    }

    [Fact]
    public async Task Instances_that_existed_before_the_agent_connected_arrive_in_the_first_snapshot()
    {
        // Arrange
        await using var controller = new ControllerNode().Start();
        await using var agent = new AgentNode().ConnectingTo(controller).WithInstances("pod-1", "pod-2");

        // Act
        agent.Start();

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds().ShouldBe(["pod-1", "pod-2"]));
    }

    [Fact]
    public async Task An_instance_removed_on_the_agent_is_shown_as_gone_on_the_controller()
    {
        // Arrange
        await using var controller = new ControllerNode().Start();
        await using var agent = new AgentNode().ConnectingTo(controller).WithInstances("pod-1").Start();
        await Eventually.AssertAsync(() => controller.InstanceIds().ShouldBe(["pod-1"]));

        // Act
        agent.Store.Remove("pod-1");
        agent.Store.Publish();

        // Assert
        await Eventually.AssertAsync(() => controller.Registry.GetInstances(Scope).Single().Lifecycle.ShouldBe(InstanceLifecycle.Gone));
    }

    [Fact]
    public async Task An_agent_with_a_wrong_token_never_gets_its_state_onto_the_controller()
    {
        // Arrange
        await using var controller = new ControllerNode().Start();
        await using var agent = new AgentNode().ConnectingTo(controller).WithToken("wrong").WithInstances("pod-1");

        // Act
        agent.Start();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        controller.Registry.GetScopeNames().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_restarted_controller_gets_the_complete_state_again_without_any_action()
    {
        // Arrange
        var first = new ControllerNode().Start();
        var port = first.Endpoint.Port;
        await using var agent = new AgentNode().ConnectingTo(first).WithInstances("pod-1", "pod-2").Start();
        await Eventually.AssertAsync(() => first.InstanceIds().ShouldBe(["pod-1", "pod-2"]));

        // Act
        await first.DisposeAsync();
        agent.Store.Upsert(AgentNode.Instance("pod-3"));
        agent.Store.Publish();
        await using var second = new ControllerNode().OnPort(port).Start();

        // Assert
        await Eventually.AssertAsync(() => second.InstanceIds().ShouldBe(["pod-1", "pod-2", "pod-3"]));
    }

    [Fact]
    public async Task The_agent_feeds_every_replica_listed_by_the_controller_from_a_single_bootstrap_address()
    {
        // Arrange
        await using var second = new ControllerNode().Start();
        await using var first = new ControllerNode().Start();
        first.AnnounceReplicas(first, second);
        second.AnnounceReplicas(first, second);
        await using var agent = new AgentNode().ConnectingTo(first).Start();

        // Act
        agent.Store.Upsert(AgentNode.Instance("pod-1"));
        agent.Store.Publish();

        // Assert
        await Eventually.AssertAsync(() =>
        {
            first.InstanceIds().ShouldBe(["pod-1"]);
            second.InstanceIds().ShouldBe(["pod-1"]);
        });
    }

    /// <summary>A controller replica: registry, token authentication and the agent listener on a loopback port.</summary>
    private sealed class ControllerNode : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private int _port;
        private ServiceProvider? _provider;
        private Task? _running;

        public IPEndPoint Endpoint { get; private set; } = new(IPAddress.Loopback, 0);

        public IScopeRegistry Registry => _provider!.GetRequiredService<IScopeRegistry>();

        public ControllerNode OnPort(int port)
        {
            _port = port;
            return this;
        }

        /// <summary>Makes the controller hand these replicas to agents as the full list to connect to; call after <see cref="Start"/>.</summary>
        public void AnnounceReplicas(params ControllerNode[] replicas) =>
            _provider!.GetRequiredService<AgentLinkOptions>().ControllerReplicas = [.. replicas.Select(r => r.Address)];

        public ControllerNode Start()
        {
            _provider = new ServiceCollection()
                .AddRegistry()
                .AddLiveness()
                .AddSecurity(o => o.AgentTokens.Add(new AgentTokenEntry(Scope, AgentTokenHash.Compute(Token))))
                .AddAgentLink(o =>
                {
                    o.ListenAddress = "127.0.0.1";
                    o.Port = _port;
                })
                .BuildServiceProvider();

            var listener = _provider.GetRequiredService<IAgentListener>();
            Endpoint = listener.Start();
            _running = listener.RunAsync(_stop.Token);
            return this;
        }

        public string Address => $"127.0.0.1:{Endpoint.Port}";

        public IReadOnlyList<string> InstanceIds() => [.. Registry.GetInstances(Scope).Select(i => i.Id)];

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_running is not null)
            {
                await _running.WaitAsync(TimeSpan.FromSeconds(10));
            }

            _provider?.Dispose();
        }
    }

    /// <summary>An agent: scope state plus the link to the controller replicas it is pointed at.</summary>
    private sealed class AgentNode : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly List<string> _ids = [];
        private string _address = "";
        private string _token = Token;
        private ServiceProvider? _provider;
        private Task? _running;

        public IScopeStateStore Store => _provider!.GetRequiredService<IScopeStateStore>();

        public AgentNode ConnectingTo(ControllerNode controller)
        {
            _address = controller.Address;
            return this;
        }

        public AgentNode WithToken(string token)
        {
            _token = token;
            return this;
        }

        public AgentNode WithInstances(params string[] ids)
        {
            _ids.AddRange(ids);
            return this;
        }

        public static InstanceInfo Instance(string id) =>
            new(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>());

        public AgentNode Start()
        {
            _provider = new ServiceCollection()
                .AddScopeState(o => o.ScopeName = Scope)
                .AddControllerLink(o =>
                {
                    o.ControllerAddresses = [_address];
                    o.Token = _token;
                    o.ReconnectMinDelay = TimeSpan.FromMilliseconds(50);
                    o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(200);
                })
                .BuildServiceProvider();

            foreach (var id in _ids)
            {
                Store.Upsert(Instance(id));
            }

            Store.Publish();
            _running = _provider.GetRequiredService<IControllerLink>().RunAsync(_stop.Token);
            return this;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            if (_running is not null)
            {
                await _running.WaitAsync(TimeSpan.FromSeconds(10));
            }

            _provider?.Dispose();
        }
    }

    /// <summary>Retries an assertion until it holds or time runs out, for state that arrives asynchronously.</summary>
    private static class Eventually
    {
        public static async Task AssertAsync(Action assertion)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                try
                {
                    assertion();
                    return;
                }
                catch (ShouldAssertException) when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25, TestContext.Current.CancellationToken);
                }
            }
        }
    }
}
