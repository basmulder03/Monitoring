using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Controller.Hosting;
using Monitoring.Hosting;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Versioning;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Hosting;

public class ControllerCompositionTests
{
    [Fact]
    public void CreateHost_registers_every_controller_slice()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").Build();

        // Act
        var services = host.Services;

        // Assert
        services.GetService<IScopeRegistry>().ShouldNotBeNull();
        services.GetService<IHealthRollup>().ShouldNotBeNull();
        services.GetService<IAlertEvaluator>().ShouldNotBeNull();
        services.GetService<IAlertState>().ShouldNotBeNull();
        services.GetService<IAgentListener>().ShouldNotBeNull();
        services.GetService<IAgentAuthenticator>().ShouldNotBeNull();
    }

    [Fact]
    public void CreateHost_runs_the_registry_purge_alert_evaluation_listener_and_web_server_in_the_background()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").Build();

        // Act
        var hosted = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        hosted.ShouldBe(["RegistryPurgeService", "AlertEvaluationService", "AgentListenerService", "GenericWebHostService", "PlainTransportWarning", "TopologySampleService", "ConfigurationReloadService"], ignoreOrder: true);
    }

    [Fact]
    public void CreateHost_refuses_to_start_without_any_agent_token()
    {
        // Arrange
        var settings = new ControllerSettings();

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Controller:AgentTokens'");
    }

    [Theory]
    [InlineData("Scope")]
    [InlineData("TokenHash")]
    public void CreateHost_names_the_missing_part_of_an_agent_token(string missing)
    {
        // Arrange
        var settings = new ControllerSettings().With($"Controller:AgentTokens:0:{(missing == "Scope" ? "TokenHash" : "Scope")}", "x");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain($"'Controller:AgentTokens:0:{missing}'");
    }

    [Fact]
    public void CreateHost_rejects_a_token_hash_that_is_not_a_sha256_hash()
    {
        // Arrange
        var settings = new ControllerSettings().With("Controller:AgentTokens:0:Scope", "shop").With("Controller:AgentTokens:0:TokenHash", "secret");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void CreateHost_hands_the_registry_settings_to_the_registry()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Registry:GoneRetention", "00:20:00")
            .With("Controller:Registry:PurgeInterval", "00:00:10")
            .Build();

        // Act
        var options = host.Services.GetRequiredService<RegistryOptions>();

        // Assert
        options.GoneRetention.ShouldBe(TimeSpan.FromMinutes(20));
        options.PurgeInterval.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CreateHost_hands_the_alerting_settings_to_alerting()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Alerting:StartupGrace", "00:01:00")
            .With("Controller:Alerting:InstanceUnhealthyWait", "00:00:45")
            .With("Controller:Alerting:ServiceDownWait", "00:00:05")
            .With("Controller:Alerting:ScopeUnknownWait", "00:03:00")
            .With("Controller:Alerting:EvaluationInterval", "00:00:02")
            .Build();

        // Act
        var options = host.Services.GetRequiredService<AlertingOptions>();

        // Assert
        options.StartupGrace.ShouldBe(TimeSpan.FromMinutes(1));
        options.InstanceUnhealthyWait.ShouldBe(TimeSpan.FromSeconds(45));
        options.ServiceDownWait.ShouldBe(TimeSpan.FromSeconds(5));
        options.ScopeUnknownWait.ShouldBe(TimeSpan.FromMinutes(3));
        options.EvaluationInterval.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void CreateHost_reads_the_silences()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Alerting:Silences:0:Rule", "service-down")
            .With("Controller:Alerting:Silences:0:Subject", "shop/")
            .With("Controller:Alerting:Silences:0:Until", "2026-11-02T06:00:00Z")
            .With("Controller:Alerting:Silences:0:Reason", "maintenance")
            .Build();

        // Act
        var silence = host.Services.GetRequiredService<AlertingOptions>().Silences.Single();

        // Assert
        silence.Rule.ShouldBe("service-down");
        silence.SubjectPrefix.ShouldBe("shop/");
        silence.Until.ShouldBe(new DateTimeOffset(2026, 11, 2, 6, 0, 0, TimeSpan.Zero));
        silence.Reason.ShouldBe("maintenance");
    }

    [Fact]
    public void CreateHost_refuses_a_silence_without_an_end()
    {
        // Arrange
        var settings = new ControllerSettings().WithToken("shop", "secret").With("Controller:Alerting:Silences:0:Rule", "service-down");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("Controller:Alerting:Silences:0:Until");
    }

    [Fact]
    public void CreateHost_hands_the_listener_settings_to_the_agent_link()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Listen:Address", "127.0.0.1")
            .With("Controller:Listen:Port", "9555")
            .With("Controller:AgentLink:HelloTimeout", "00:00:03")
            .With("Controller:Replicas", "c1:9440, c2:9440")
            .Build();

        // Act
        var options = host.Services.GetRequiredService<AgentLinkOptions>();

        // Assert
        options.ListenAddress.ShouldBe("127.0.0.1");
        options.Port.ShouldBe(9555);
        options.HelloTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        options.ControllerReplicas.ShouldBe(["c1:9440", "c2:9440"]);
    }

    [Fact]
    public void CreateHost_hands_the_dashboard_settings_to_the_dashboard()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Dashboard:PushInterval", "00:00:02")
            .With("Controller:Dashboard:KeepAliveInterval", "00:00:07")
            .With("Controller:Dashboard:GroupBy", "environment, site")
            .Build();

        // Act
        var options = host.Services.GetRequiredService<DashboardOptions>();

        // Assert
        options.PushInterval.ShouldBe(TimeSpan.FromSeconds(2));
        options.KeepAliveInterval.ShouldBe(TimeSpan.FromSeconds(7));
        options.GroupBy.ShouldBe(["environment", "site"]);
    }

    [Fact]
    public void CreateHost_groups_by_environment_and_datacenter_when_nothing_is_set()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").Build();

        // Act
        var options = host.Services.GetRequiredService<DashboardOptions>();

        // Assert
        options.GroupBy.ShouldBe(["environment", "datacenter"]);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    public void CreateHost_turns_grouping_off_when_asked_to(string value)
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").With("Controller:Dashboard:GroupBy", value).Build();

        // Act
        var options = host.Services.GetRequiredService<DashboardOptions>();

        // Assert
        options.GroupBy.ShouldBeEmpty();
    }

    [Fact]
    public void CreateHost_keeps_the_slice_defaults_for_settings_that_are_not_given()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").Build();

        // Act
        var options = host.Services.GetRequiredService<AgentLinkOptions>();

        // Assert
        options.ListenAddress.ShouldBe("0.0.0.0");
        options.Port.ShouldBe(9440);
    }

    [Fact]
    public void CreateHost_names_a_port_that_is_not_a_number()
    {
        // Arrange
        var settings = new ControllerSettings().WithToken("shop", "secret").With("Controller:Listen:Port", "http");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Controller:Listen:Port'");
    }

    [Fact]
    public void CreateHost_rejects_a_port_out_of_range()
    {
        // Arrange
        var settings = new ControllerSettings().WithToken("shop", "secret").With("Controller:Listen:Port", "70000");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task A_started_controller_accepts_an_authenticated_agent_and_announces_its_replicas()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Listen:Address", "127.0.0.1")
            .With("Controller:Listen:Port", "0")
            .With("Controller:Replicas", "c1:9440,c2:9440")
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var endpoint = host.Services.GetRequiredService<IAgentListener>().LocalEndPoint!;
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint, TestContext.Current.CancellationToken);
        var stream = client.GetStream();

        // Act
        await FrameCodec.WriteAsync(stream, new Hello(ProtocolVersion.Current, "agent-1", "agent", null, new Dictionary<string, string>(), "secret"), ct: TestContext.Current.CancellationToken);
        var reply = await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken);

        // Assert
        reply.ShouldBeOfType<Config>().ControllerReplicas.ShouldBe(["c1:9440", "c2:9440"]);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_started_controller_serves_the_dashboard_api()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret").Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });

        // Act
        using var response = await http.GetAsync($"{address}/api/overview", TestContext.Current.CancellationToken);

        // Assert
        response.IsSuccessStatusCode.ShouldBeTrue();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("not an address")]
    public void CreateHost_names_a_web_address_that_is_not_an_ip_address(string address)
    {
        // Arrange
        var settings = new ControllerSettings().WithToken("shop", "secret").With("Controller:Web:Address", address);

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Controller:Web:Address'");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    public void CreateHost_names_a_web_port_out_of_range(string port)
    {
        // Arrange
        var settings = new ControllerSettings().WithToken("shop", "secret").With("Controller:Web:Port", port);

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Controller:Web:Port'");
    }

    [Fact]
    public async Task A_started_controller_stops_cleanly()
    {
        // Arrange
        using var host = new ControllerSettings().WithToken("shop", "secret")
            .With("Controller:Listen:Address", "127.0.0.1")
            .With("Controller:Listen:Port", "0")
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var stop = async () => await host.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Collects controller settings and builds the host from them, ignoring any configuration file on the machine.</summary>
    private sealed class ControllerSettings
    {
        private readonly Dictionary<string, string?> _settings = new()
        {
            ["Controller:Web:Address"] = "127.0.0.1",
            ["Controller:Web:Port"] = "0",
        };

        private int _tokens;

        public ControllerSettings With(string key, string value)
        {
            _settings[key] = value;
            return this;
        }

        /// <summary>Accepts agents that present the token and may report as the scope.</summary>
        public ControllerSettings WithToken(string scope, string token)
        {
            _settings[$"Controller:AgentTokens:{_tokens}:Scope"] = scope;
            _settings[$"Controller:AgentTokens:{_tokens}:TokenHash"] = AgentTokenHash.Compute(token);
            _tokens++;
            return this;
        }

        public IHost Build() =>
            ControllerComposition.CreateHost([], _settings, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
    }
}
