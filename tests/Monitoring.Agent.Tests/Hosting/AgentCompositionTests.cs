using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Hosting;
using Monitoring.Hosting;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Hosting;

public class AgentCompositionTests
{
    [Fact]
    public void CreateHost_registers_the_scope_state_and_the_controller_link()
    {
        // Arrange
        using var host = new AgentSettings().Valid().Build();

        // Act
        var services = host.Services;

        // Assert
        services.GetService<IScopeStateStore>().ShouldNotBeNull();
        services.GetService<IControllerLink>().ShouldNotBeNull();
    }

    [Fact]
    public void CreateHost_runs_the_controller_link_in_the_background()
    {
        // Arrange
        using var host = new AgentSettings().Valid().Build();

        // Act
        var hosted = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        hosted.ShouldBe(["ControllerLinkHostedService"]);
    }

    [Theory]
    [InlineData("Agent:ScopeName")]
    [InlineData("Agent:Token")]
    [InlineData("Agent:Controllers")]
    public void CreateHost_names_the_required_setting_that_is_missing(string missing)
    {
        // Arrange
        var settings = new AgentSettings().Valid().Without(missing);

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain($"'{missing}'");
    }

    [Fact]
    public void CreateHost_uses_the_scope_name_for_the_scope_state()
    {
        // Arrange
        using var host = new AgentSettings().Valid().With("Agent:ScopeName", "orders-platform").Build();

        // Act
        var scope = host.Services.GetRequiredService<IScopeStateStore>().ScopeName;

        // Assert
        scope.ShouldBe("orders-platform");
    }

    [Fact]
    public void CreateHost_hands_the_connection_settings_to_the_controller_link()
    {
        // Arrange
        using var host = new AgentSettings().Valid()
            .With("Agent:Controllers", "c1:9440, c2:9440")
            .With("Agent:Id", "agent-7")
            .With("Agent:Version", "1.2.3")
            .With("Agent:Labels:env", "prod")
            .With("Agent:Labels:site", "a")
            .With("Agent:Reconnect:MinDelay", "00:00:02")
            .With("Agent:Reconnect:MaxDelay", "00:01:00")
            .With("Agent:ConfigReplyTimeout", "00:00:05")
            .With("Agent:SnapshotInterval", "00:10:00")
            .Build();

        // Act
        var options = host.Services.GetRequiredService<ControllerLinkOptions>();

        // Assert
        options.ControllerAddresses.ShouldBe(["c1:9440", "c2:9440"]);
        (options.AgentId, options.AgentVersion, options.Token).ShouldBe(("agent-7", "1.2.3", "secret"));
        options.Labels.ShouldBe(new Dictionary<string, string> { ["env"] = "prod", ["site"] = "a" });
        (options.ReconnectMinDelay, options.ReconnectMaxDelay, options.ConfigReplyTimeout, options.SnapshotInterval)
            .ShouldBe((TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void CreateHost_rejects_a_controller_address_that_is_not_host_and_port()
    {
        // Arrange
        var settings = new AgentSettings().Valid().With("Agent:Controllers", "controller");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public async Task A_started_agent_introduces_itself_to_the_controller_with_its_settings()
    {
        // Arrange
        using var controller = new TcpListener(IPAddress.Loopback, 0);
        controller.Start();
        var port = ((IPEndPoint)controller.LocalEndpoint).Port;
        using var host = new AgentSettings().Valid()
            .With("Agent:Controllers", $"127.0.0.1:{port}")
            .With("Agent:Id", "agent-7")
            .With("Agent:Labels:env", "prod")
            .Build();

        // Act
        await host.StartAsync(TestContext.Current.CancellationToken);
        using var accepted = await controller.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var hello = await FrameCodec.ReadAsync(accepted.GetStream(), ct: TestContext.Current.CancellationToken);

        // Assert
        var message = hello.ShouldBeOfType<Hello>();
        (message.InstanceId, message.Token).ShouldBe(("agent-7", "secret"));
        message.Labels.ShouldBe(new Dictionary<string, string> { ["env"] = "prod" });
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Collects agent settings and builds the host from them, ignoring any configuration file on the machine.</summary>
    private sealed class AgentSettings
    {
        private readonly Dictionary<string, string?> _settings = [];

        public AgentSettings With(string key, string value)
        {
            _settings[key] = value;
            return this;
        }

        public AgentSettings Without(string key)
        {
            _settings.Remove(key);
            return this;
        }

        /// <summary>The smallest set of settings that starts an agent.</summary>
        public AgentSettings Valid() =>
            With("Agent:ScopeName", "shop").With("Agent:Token", "secret").With("Agent:Controllers", "controller:9440");

        public IHost Build() =>
            AgentComposition.CreateHost([], _settings, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
    }
}
