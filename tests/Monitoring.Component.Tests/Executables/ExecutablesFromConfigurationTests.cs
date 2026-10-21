using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Hosting;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Controller.Hosting;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Executables;

/// <summary>Whole controller and agent hosts, built from configuration alone, talking over loopback TCP.</summary>
public class ExecutablesFromConfigurationTests
{
    private const string EnrollmentToken = "enroll";

    [Fact]
    public async Task Agents_register_themselves_under_their_own_scope_with_a_single_enrollment_token()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(EnrollmentToken).Start();
        await using var shop = new AgentHost("shop", EnrollmentToken).ConnectingTo(controller).Start();
        await using var billing = new AgentHost("billing", EnrollmentToken).ConnectingTo(controller).Start();

        // Act
        shop.Report("shop-pod-1");
        billing.Report("billing-pod-1");

        // Assert
        await Eventually.AssertAsync(() =>
        {
            controller.Registry.GetScopeNames().ShouldBe(["billing", "shop"]);
            controller.InstanceIds("shop").ShouldBe(["shop-pod-1"]);
            controller.InstanceIds("billing").ShouldBe(["billing-pod-1"]);
        });
    }

    [Fact]
    public async Task An_agent_with_a_token_bound_to_another_scope_is_not_registered()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingScope("shop", "shop-secret").Start();
        await using var agent = new AgentHost("billing", "shop-secret").ConnectingTo(controller).Start();

        // Act
        agent.Report("billing-pod-1");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        controller.Registry.GetScopeNames().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bound_token_still_registers_an_agent_for_its_own_scope()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingScope("shop", "shop-secret").Start();
        await using var agent = new AgentHost("shop", "shop-secret").ConnectingTo(controller).Start();

        // Act
        agent.Report("shop-pod-1");

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["shop-pod-1"]));
    }

    [Fact]
    public async Task An_agent_with_an_unknown_token_is_not_registered()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(EnrollmentToken).Start();
        await using var agent = new AgentHost("shop", "guess").ConnectingTo(controller).Start();

        // Act
        agent.Report("shop-pod-1");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        controller.Registry.GetScopeNames().ShouldBeEmpty();
    }

    /// <summary>A controller host built from settings, listening on a free loopback port.</summary>
    private sealed class ControllerHost : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> _settings = new()
        {
            ["Controller:Listen:Address"] = "127.0.0.1",
            ["Controller:Listen:Port"] = "0",
        };

        private int _tokens;
        private IHost? _host;

        public IScopeRegistry Registry => _host!.Services.GetRequiredService<IScopeRegistry>();

        public string Address => $"127.0.0.1:{_host!.Services.GetRequiredService<IAgentListener>().LocalEndPoint!.Port}";

        /// <summary>Accepts any agent holding the token, whatever scope it announces.</summary>
        public ControllerHost AcceptingEnrollment(string token) => AcceptingScope(AgentTokenEntry.AnyScope, token);

        /// <summary>Accepts agents holding the token, but only as the given scope.</summary>
        public ControllerHost AcceptingScope(string scope, string token)
        {
            _settings[$"Controller:AgentTokens:{_tokens}:Scope"] = scope;
            _settings[$"Controller:AgentTokens:{_tokens}:TokenHash"] = AgentTokenHash.Compute(token);
            _tokens++;
            return this;
        }

        public ControllerHost Start()
        {
            _host = ControllerComposition.CreateHost([], _settings, MissingFile());
            _host.StartAsync().GetAwaiter().GetResult();
            return this;
        }

        public IReadOnlyList<string> InstanceIds(string scope) => [.. Registry.GetInstances(scope).Select(i => i.Id)];

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }

    /// <summary>An agent host built from settings, pointed at a controller.</summary>
    private sealed class AgentHost(string scope, string token) : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> _settings = new()
        {
            ["Agent:Reconnect:MinDelay"] = "00:00:00.050",
            ["Agent:Reconnect:MaxDelay"] = "00:00:00.200",
        };

        private IHost? _host;

        public AgentHost ConnectingTo(ControllerHost controller)
        {
            _settings["Agent:Controllers"] = controller.Address;
            return this;
        }

        public AgentHost Start()
        {
            _settings["Agent:ScopeName"] = scope;
            _settings["Agent:Token"] = token;
            _host = AgentComposition.CreateHost([], _settings, MissingFile());
            _host.StartAsync().GetAwaiter().GetResult();
            return this;
        }

        /// <summary>Makes the agent report a running instance, as discovery will.</summary>
        public void Report(string instanceId)
        {
            var store = _host!.Services.GetRequiredService<IScopeStateStore>();
            store.Upsert(new InstanceInfo(instanceId, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));
            store.Publish();
        }

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }

    private static string MissingFile() => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

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
