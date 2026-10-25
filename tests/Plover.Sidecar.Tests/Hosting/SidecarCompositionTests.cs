using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Hosting;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Sidecar.Features.AgentLink;
using Plover.Sidecar.Features.LibraryLink;
using Plover.Sidecar.Features.Probing;
using Plover.Sidecar.Hosting;
using Plover.Sidecar.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Sidecar.Tests.Hosting;

public class SidecarCompositionTests
{
    [Fact]
    public void CreateHost_runs_the_probing_and_the_registration_in_the_background()
    {
        // Arrange
        using var host = new SidecarSettings().Valid().Build();

        // Act
        var hosted = host.Services.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        hosted.ShouldBe(["ProbeService", "SidecarService"], ignoreOrder: true);
    }

    [Fact]
    public async Task CreateHost_serves_the_values_of_the_sidecar_to_prometheus_when_a_port_is_set()
    {
        // Arrange
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var host = new SidecarSettings().Valid().With("Sidecar:Prometheus:Port", port.ToString()).Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        // Act
        using var http = new HttpClient();
        var text = await http.GetStringAsync($"http://127.0.0.1:{port}/metrics", TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        text.ShouldContain("plover_sidecar_memory_bytes ");
    }

    [Fact]
    public void CreateHost_names_the_required_agent_setting_when_it_is_missing()
    {
        // Arrange
        var settings = new SidecarSettings();

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Sidecar:Agent'");
    }

    [Fact]
    public void CreateHost_rejects_an_agent_address_that_is_not_host_and_port()
    {
        // Arrange
        var settings = new SidecarSettings().With("Sidecar:Agent", "agent");

        // Act
        var build = () => settings.Build();

        // Assert
        build.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void CreateHost_hands_the_settings_to_the_slices()
    {
        // Arrange
        using var host = new SidecarSettings().Valid()
            .With("Sidecar:InstanceId", "orders-7d9f-abc12")
            .With("Sidecar:Labels:team", "shop")
            .With("Sidecar:Library:Socket", "/run/app.sock")
            .With("Sidecar:Library:CallTimeout", "00:00:03")
            .With("Sidecar:Probe:Interval", "00:00:02")
            .With("Sidecar:Reconnect:MinDelay", "00:00:02")
            .With("Sidecar:Reconnect:MaxDelay", "00:01:00")
            .With("Sidecar:ConfigReplyTimeout", "00:00:07")
            .Build();

        // Act
        var link = host.Services.GetRequiredService<LibraryLinkOptions>();
        var probe = host.Services.GetRequiredService<ProbeOptions>();
        var agent = host.Services.GetRequiredService<AgentLinkOptions>();

        // Assert
        link.SocketPath.ShouldBe("/run/app.sock");
        link.CallTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        probe.Interval.ShouldBe(TimeSpan.FromSeconds(2));
        agent.AgentAddress.ShouldBe("agent:9441");
        agent.InstanceId.ShouldBe("orders-7d9f-abc12");
        agent.Labels["team"].ShouldBe("shop");
        agent.ReconnectMinDelay.ShouldBe(TimeSpan.FromSeconds(2));
        agent.ReconnectMaxDelay.ShouldBe(TimeSpan.FromMinutes(1));
        agent.ConfigReplyTimeout.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void CreateHost_sends_what_the_downward_api_tells_about_the_pod_as_labels_without_overriding_a_label_set_by_hand()
    {
        // Arrange
        using var host = new SidecarSettings().Valid()
            .With("Sidecar:Pod:Ip", "10.1.2.3")
            .With("Sidecar:Pod:Node", "node-a")
            .With("Sidecar:Pod:Namespace", "shop")
            .With("Sidecar:Labels:namespace", "by-hand")
            .Build();

        // Act
        var agent = host.Services.GetRequiredService<AgentLinkOptions>();

        // Assert
        agent.Labels["pod.ip"].ShouldBe("10.1.2.3");
        agent.Labels["node"].ShouldBe("node-a");
        agent.Labels["namespace"].ShouldBe("by-hand");
    }

    [Fact]
    public void CreateHost_uses_the_host_name_as_instance_id_unless_told_otherwise()
    {
        // Arrange
        using var host = new SidecarSettings().Valid().Build();

        // Act
        var agent = host.Services.GetRequiredService<AgentLinkOptions>();

        // Assert: in Kubernetes the host name of a pod is its name
        agent.InstanceId.ShouldBe(Environment.MachineName);
    }

    [Fact]
    public void CreateHost_looks_for_the_application_on_the_shared_volume_by_default()
    {
        // Arrange
        using var host = new SidecarSettings().Valid().Build();

        // Act
        var link = host.Services.GetRequiredService<LibraryLinkOptions>();

        // Assert
        link.SocketPath.ShouldBe("/var/run/plover/plover.sock");
    }

    [Fact]
    public async Task A_started_sidecar_registers_the_application_with_the_agent_and_keeps_sending_heartbeats()
    {
        // Arrange
        await using var app = new FakeApplication { Description = new InstanceDescription("orders", "1.2.3"), Health = new HealthReport(HealthState.Degraded, []) }.Start();
        using var agent = new TcpListener(IPAddress.Loopback, 0);
        agent.Start();
        using var host = new SidecarSettings()
            .With("Sidecar:Agent", $"127.0.0.1:{((IPEndPoint)agent.LocalEndpoint).Port}")
            .With("Sidecar:Library:Socket", app.SocketPath)
            .With("Sidecar:InstanceId", "orders-1")
            .With("Sidecar:Probe:Interval", "00:00:00.200")
            .Build();

        // Act
        await host.StartAsync(TestContext.Current.CancellationToken);
        using var accepted = await agent.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        var stream = accepted.GetStream();
        var hello = await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken);
        await FrameCodec.WriteAsync(stream, new Config("v1", [], HeartbeatSeconds: 1), ct: TestContext.Current.CancellationToken);
        var heartbeat = await FrameCodec.ReadAsync(stream, ct: TestContext.Current.CancellationToken);

        // Assert
        var registration = hello.ShouldBeOfType<Hello>();
        registration.InstanceId.ShouldBe("orders-1");
        registration.ServiceName.ShouldBe("orders");
        registration.Version.ShouldBe("1.2.3");
        heartbeat.ShouldBe(new Heartbeat(1, HealthState.Degraded));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Collects sidecar settings and builds the host from them, ignoring any configuration file on the machine.</summary>
    private sealed class SidecarSettings
    {
        private readonly Dictionary<string, string?> _settings = [];

        public SidecarSettings With(string key, string value)
        {
            _settings[key] = value;
            return this;
        }

        /// <summary>The smallest set of settings that starts a sidecar.</summary>
        public SidecarSettings Valid() => With("Sidecar:Agent", "agent:9441");

        public IHost Build() =>
            SidecarComposition.CreateHost([], _settings, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
    }
}
