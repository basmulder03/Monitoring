using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Agent.Features.Configuration;
using Monitoring.Component.Tests.Support;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Topology;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Sidecars;

/// <summary>What an application says about itself, from its client through its sidecar and a real agent to a real controller.</summary>
public class ApplicationToControllerTests
{
    private const string Token = "enroll";

    /// <summary>The controller asks agents to pull as often as it lets them, so the tests do not wait long.</summary>
    private const string FastPulls = """{ "defaults": { "pullSeconds": 1, "edgeFlushSeconds": 1 } }""";

    [Fact]
    public async Task A_failing_health_check_of_the_application_shows_on_the_controller()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();

        // Act
        await using var application = new ApplicationHost("orders-1").WithCheck("database", HealthStatus.Unhealthy).Start(agent);

        // Assert
        await Eventually.AssertAsync(() =>
        {
            var instance = controller.Registry.GetInstances("shop").Single();
            instance.Health.ShouldBe(HealthState.Unhealthy);
            instance.Version.ShouldBe("1.2.3");
            instance.Checks!.Single().Name.ShouldBe("database");
        });
    }

    [Fact]
    public async Task The_metrics_of_the_application_show_on_the_controller()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();

        // Act
        await using var application = new ApplicationHost("orders-1").Start(agent);

        // Assert
        await Eventually.AssertAsync(() =>
            controller.Registry.GetInstances("shop").Single().Metrics!.ShouldContain(s => s.Name == "dotnet.process.memory.working_set"));
    }

    [Fact]
    public async Task The_controller_can_ask_the_agent_for_the_recent_values_of_the_metrics_of_an_instance()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();
        await using var application = new ApplicationHost("orders-1").Start(agent);
        await Eventually.AssertAsync(() =>
            controller.Registry.GetInstances("shop").Single().Metrics!.ShouldContain(s => s.Name == "dotnet.process.memory.working_set"));

        // Act
        var json = await controller.Services.GetRequiredService<IAgentRequests>().GetMetricHistoryAsync("shop", "orders-1", TestContext.Current.CancellationToken);

        // Assert
        var history = ReportPayload.ReadHistory(json)!;
        history.Series.Single(s => s.Name == "dotnet.process.memory.working_set").Values.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_calls_of_the_application_to_its_dependencies_show_on_the_controller()
    {
        // Arrange
        var sourceName = $"app-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();
        await using var application = new ApplicationHost("orders-1").WithTraceSource(sourceName).Start(agent);

        // Act
        for (var i = 0; i < 3; i++)
        {
            using var call = source.StartActivity("GET", ActivityKind.Client);
            call!.SetTag("http.request.method", "GET");
            call.SetTag("server.address", "payments");
            call.SetTag("server.port", 8080);
        }

        // Assert
        await Eventually.AssertAsync(() =>
        {
            var edge = controller.Registry.GetInstances("shop").Single().Edges!.Single(e => e.Target == "payments:8080");
            edge.Target.ShouldBe("payments:8080");
            edge.Calls.ShouldBe(3);
        });
    }

    [Fact]
    public async Task A_raw_tcp_connection_of_the_application_shows_as_an_edge_to_the_peer_address_on_the_controller()
    {
        // Arrange
        using var peer = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        peer.Start();
        var address = $"127.0.0.1:{((System.Net.IPEndPoint)peer.LocalEndpoint).Port}";
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).With("Controller:Topology:SampleInterval", "00:00:00.100").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();
        await using var application = new ApplicationHost("orders-1").Start(agent);

        // Act
        using (await Monitoring.Client.Features.Tracing.MonitoredTcp.ConnectAsync("127.0.0.1", ((System.Net.IPEndPoint)peer.LocalEndpoint).Port, TestContext.Current.CancellationToken))
        {
        }

        // Assert
        await Eventually.AssertAsync(() =>
        {
            controller.Registry.GetInstances("shop").Single().Edges!.ShouldContain(e => e.Type == "TCP" && e.Target == address && e.Calls == 1);
            var graph = controller.Services.GetRequiredService<ITopology>().GetGraph();
            graph.Edges.Single(e => e.Type == "TCP").To.ShouldBe($"external:{address}");
        });
    }

    [Fact]
    public async Task A_queue_between_the_sending_and_the_receiving_side_of_an_application_shows_in_the_graph_with_the_trace_carried_across()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).With("Controller:Topology:SampleInterval", "00:00:00.100").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();
        await using var application = new ApplicationHost("orders-1").WithTraceSource(Monitoring.Client.Features.Tracing.MonitoredMessaging.SourceName).Start(agent);
        var message = new Dictionary<string, string>();

        // Act
        using (Monitoring.Client.Features.Tracing.MonitoredMessaging.StartSend("activemq", "invoices", message.Add))
        {
        }

        using (Monitoring.Client.Features.Tracing.MonitoredMessaging.StartProcess("activemq", "invoices", name => message.GetValueOrDefault(name)))
        {
        }

        // Assert
        await Eventually.AssertAsync(() =>
        {
            var edges = controller.Services.GetRequiredService<ITopology>().GetGraph().Edges;
            edges.ShouldContain(e => e.From == "service:shop/orders" && e.To == "queue:activemq://invoices");
            edges.ShouldContain(e => e.From == "queue:activemq://invoices" && e.To == "service:shop/orders");
        });
    }

    [Fact]
    public async Task The_dependency_graph_of_the_controller_shows_what_the_application_called()
    {
        // Arrange
        var sourceName = $"app-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).With("Controller:Topology:SampleInterval", "00:00:00.100").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01").Start();
        await using var application = new ApplicationHost("orders-1").WithTraceSource(sourceName).Start(agent);

        // Act
        using (var call = source.StartActivity("query", ActivityKind.Client))
        {
            call!.SetTag("db.system", "mssql");
            call.SetTag("server.address", "db1");
            call.SetTag("db.namespace", "orders");
        }

        // Assert
        await Eventually.AssertAsync(() =>
        {
            var graph = controller.Services.GetRequiredService<ITopology>().GetGraph();
            graph.Edges.Single(e => e.To.StartsWith("database:mssql://db1", StringComparison.Ordinal)).From.ShouldBe("service:shop/orders");
        });
    }

    [Fact]
    public async Task A_change_of_the_central_configuration_reaches_the_agent_without_a_restart()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration("""{ "defaults": { "pullSeconds": 30 } }""").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).Start();
        var configuration = agent.Services.GetRequiredService<IAgentConfiguration>();
        await Eventually.AssertAsync(() => configuration.Current.PullSeconds.ShouldBe(30));

        // Act
        controller.ChangeCentralConfiguration("""{ "defaults": { "pullSeconds": 45 }, "overrides": [ { "match": { "scope": "shop" }, "set": { "edgeFlushSeconds": 7 } } ] }""");

        // Assert
        await Eventually.AssertAsync(() =>
        {
            configuration.Current.PullSeconds.ShouldBe(45);
            configuration.Current.EdgeFlushSeconds.ShouldBe(7);
        });
    }

    [Fact]
    public async Task A_central_configuration_that_is_not_valid_leaves_the_agent_with_what_it_has()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration("""{ "defaults": { "pullSeconds": 30 } }""").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).Start();
        var configuration = agent.Services.GetRequiredService<IAgentConfiguration>();
        await Eventually.AssertAsync(() => configuration.Current.PullSeconds.ShouldBe(30));

        // Act
        controller.ChangeCentralConfiguration("""{ "defaults": { "pullSeconds": 0 } }""");
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        // Assert
        configuration.Current.PullSeconds.ShouldBe(30);
    }

    [Fact]
    public async Task An_application_whose_sidecar_says_goodbye_stays_as_healthy_as_the_platform_says()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithCentralConfiguration(FastPulls).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await using var application = new ApplicationHost("orders-1").WithCheck("database", HealthStatus.Healthy).Start(agent);
        await Eventually.AssertAsync(() => controller.Registry.GetInstances("shop").Single().Checks.ShouldNotBeNull());

        // Act
        await application.StopSidecarAsync();

        // Assert: a graceful stop is a rollout, not a failure
        await Eventually.AssertAsync(() => controller.Registry.GetInstances("shop").Single().Checks.ShouldBeNull());
        controller.Registry.GetInstances("shop").Single().Health.ShouldBe(HealthState.Healthy);
    }
}
