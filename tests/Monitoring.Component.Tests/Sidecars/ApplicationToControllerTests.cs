using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Component.Tests.Support;
using Monitoring.Controller.Features.Topology;
using Monitoring.Protocol.Health;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Sidecars;

/// <summary>What an application says about itself, from its client through its sidecar and a real agent to a real controller.</summary>
public class ApplicationToControllerTests
{
    private const string Token = "enroll";

    [Fact]
    public async Task A_failing_health_check_of_the_application_shows_on_the_controller()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
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
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:MetricsPullInterval", "00:00:00.100").Start();

        // Act
        await using var application = new ApplicationHost("orders-1").Start(agent);

        // Assert
        await Eventually.AssertAsync(() =>
            controller.Registry.GetInstances("shop").Single().Metrics!.ShouldContain(s => s.Name == "dotnet.process.memory.working_set"));
    }

    [Fact]
    public async Task The_calls_of_the_application_to_its_dependencies_show_on_the_controller()
    {
        // Arrange
        var sourceName = $"app-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01")
            .With("Agent:Sidecars:MetricsPullInterval", "00:00:00.100")
            .With("Agent:Sidecars:EdgesPullInterval", "00:00:00.100").Start();
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
    public async Task The_dependency_graph_of_the_controller_shows_what_the_application_called()
    {
        // Arrange
        var sourceName = $"app-{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).With("Controller:Topology:SampleInterval", "00:00:00.100").Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Sidecars:HeartbeatInterval", "00:00:01")
            .With("Agent:Sidecars:MetricsPullInterval", "00:00:00.100")
            .With("Agent:Sidecars:EdgesPullInterval", "00:00:00.100").Start();
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
    public async Task An_application_whose_sidecar_says_goodbye_stays_as_healthy_as_the_platform_says()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
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
