using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ControllerLink;

public class ControllerLinkServiceTests
{
    [Fact]
    public async Task RunAsync_connects_to_every_bootstrap_address()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("a:9440", "b:9440");

        // Act
        fixture.Start();
        await fixture.WaitForRunningAsync("a:9440", "b:9440");

        // Assert
        fixture.Running.ShouldBe(["a:9440", "b:9440"], ignoreOrder: true);
    }

    [Fact]
    public async Task RunAsync_connects_to_every_replica_the_controller_lists()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("balancer:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("balancer:9440");

        // Act
        fixture.Send("balancer:9440", new Config("v1", ["r1:9440", "r2:9440"]));
        await fixture.WaitForRunningAsync("r1:9440", "r2:9440");

        // Assert
        fixture.Running.ShouldBe(["r1:9440", "r2:9440"], ignoreOrder: true);
    }

    [Fact]
    public async Task RunAsync_disconnects_from_an_address_that_is_no_longer_listed()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("balancer:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("balancer:9440");

        // Act
        fixture.Send("balancer:9440", new Config("v1", ["r1:9440"]));
        await fixture.WaitForRunningAsync("r1:9440");

        // Assert
        fixture.IsCancelled("balancer:9440").ShouldBeTrue();
        fixture.IsCancelled("r1:9440").ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_keeps_the_connection_to_a_replica_that_is_still_listed()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("r1:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("r1:9440");

        // Act
        fixture.Send("r1:9440", new Config("v1", ["r1:9440", "r2:9440"]));
        await fixture.WaitForRunningAsync("r1:9440", "r2:9440");

        // Assert
        fixture.StartCount("r1:9440").ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ignores_an_empty_replica_list()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("balancer:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("balancer:9440");

        // Act
        fixture.Send("balancer:9440", new Config("v1", []));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.IsCancelled("balancer:9440").ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_ignores_a_replica_list_without_a_valid_address()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("balancer:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("balancer:9440");

        // Act
        fixture.Send("balancer:9440", new Config("v1", ["not an address", "no-port"]));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.IsCancelled("balancer:9440").ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_skips_invalid_addresses_but_uses_the_valid_ones()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("balancer:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("balancer:9440");

        // Act
        fixture.Send("balancer:9440", new Config("v1", ["nonsense", "r1:9440"]));
        await fixture.WaitForRunningAsync("r1:9440");

        // Assert
        fixture.Running.ShouldBe(["r1:9440"]);
    }

    [Fact]
    public async Task RunAsync_treats_addresses_differing_only_in_case_as_one_replica()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("Controller:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("Controller:9440");

        // Act
        fixture.Send("Controller:9440", new Config("v1", ["controller:9440", "other:9440"]));
        await fixture.WaitForRunningAsync("Controller:9440", "other:9440");

        // Assert
        fixture.StartCount("controller:9440").ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_stops_every_connection_and_completes_on_shutdown()
    {
        // Arrange
        var fixture = new ServiceFixture().WithBootstrap("a:9440", "b:9440");
        fixture.Start();
        await fixture.WaitForRunningAsync("a:9440", "b:9440");

        // Act
        fixture.Shutdown();

        // Assert
        await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        (fixture.IsCancelled("a:9440"), fixture.IsCancelled("b:9440")).ShouldBe((true, true));
    }

    /// <summary>Builds the service over a fake per-replica connection loop that records what the service asks of it.</summary>
    private sealed class ServiceFixture
    {
        private readonly ControllerLinkOptions _options = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Lock _gate = new();
        private readonly List<string> _started = [];
        private readonly Dictionary<string, Action<Config>> _callbacks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationToken> _tokens = new(StringComparer.OrdinalIgnoreCase);

        public Task Completion { get; private set; } = Task.CompletedTask;

        /// <summary>Addresses whose connection loop was started and has not been cancelled.</summary>
        public IReadOnlyList<string> Running
        {
            get
            {
                lock (_gate)
                {
                    return [.. _tokens.Where(t => !t.Value.IsCancellationRequested).Select(t => t.Key)];
                }
            }
        }

        public ServiceFixture WithBootstrap(params string[] addresses)
        {
            _options.ControllerAddresses = [.. addresses];
            return this;
        }

        public void Start()
        {
            var service = new ControllerLinkService(_options, RunReplicaAsync, NullLogger<ControllerLinkService>.Instance);
            Completion = service.RunAsync(_shutdown.Token);
        }

        public void Shutdown() => _shutdown.Cancel();

        /// <summary>Plays a replica at the address sending a configuration to the agent.</summary>
        public void Send(string address, Config config)
        {
            Action<Config> callback;
            lock (_gate)
            {
                callback = _callbacks[address];
            }

            callback(config);
        }

        public bool IsCancelled(string address)
        {
            lock (_gate)
            {
                return _tokens[address].IsCancellationRequested;
            }
        }

        public int StartCount(string address)
        {
            lock (_gate)
            {
                return _started.Count(a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Waits until exactly these addresses are running.</summary>
        public async Task WaitForRunningAsync(params string[] addresses)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (!Running.OrderBy(a => a).SequenceEqual(addresses.OrderBy(a => a), StringComparer.OrdinalIgnoreCase))
            {
                await Task.Delay(5, guard.Token);
            }
        }

        private async Task RunReplicaAsync(string address, Action<Config> onConfig, CancellationToken ct)
        {
            lock (_gate)
            {
                _started.Add(address);
                _callbacks[address] = onConfig;
                _tokens[address] = ct;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                // Stopped by the service.
            }
        }
    }
}
