using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Tests.Features.Alerting;
using Plover.Controller.Features.Registry;
using Plover.Controller.Features.SelfMonitoring;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.SelfMonitoring;

public class SelfMonitoringTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeScopeRegistry TwoScopes() => new FakeScopeRegistry()
        .WithInstance("shop", new InstanceInfo("a-1", "a", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0"))
        .WithInstance("blog", new InstanceInfo("b-1", "b", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0"));

    [Fact]
    public void Read_counts_the_scopes_whose_agent_is_alive()
    {
        // Arrange
        var telemetry = new SystemTelemetry(TwoScopes(), new FakeAgentLiveness().WithDeadScope("blog"), new FakeTimeProvider());

        // Act
        var status = telemetry.Read();

        // Assert
        status.Scopes.ShouldBe(2);
        status.AgentsConnected.ShouldBe(1);
        status.Healthy.ShouldBeFalse();
    }

    [Fact]
    public void Read_reports_the_frames_per_second_over_the_latest_window_and_the_size_of_what_is_held()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var telemetry = new SystemTelemetry(TwoScopes(), new FakeAgentLiveness(), time);
        for (var frame = 0; frame < 50; frame++)
        {
            telemetry.FrameReceived();
        }

        // Act
        var early = telemetry.Read();
        time.Advance(TimeSpan.FromSeconds(10));
        var later = telemetry.Read();
        time.Advance(TimeSpan.FromSeconds(1));
        var sameWindow = telemetry.Read();

        // Assert
        early.FramesPerSecond.ShouldBe(0);
        later.FramesPerSecond.ShouldBe(5);
        sameWindow.FramesPerSecond.ShouldBe(5);
        later.Instances.ShouldBe(2);
        later.Series.ShouldBe(0);
    }

    [Fact]
    public void Read_reports_the_open_streams_the_latest_evaluation_and_the_uptime()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var telemetry = new SystemTelemetry(TwoScopes(), new FakeAgentLiveness(), time);
        telemetry.EventClientConnected();
        telemetry.EventClientConnected();
        telemetry.EventClientDisconnected();
        telemetry.AlertsEvaluated(TimeSpan.FromMilliseconds(12));
        time.Advance(TimeSpan.FromSeconds(90));

        // Act
        var status = telemetry.Read();

        // Assert
        status.EventClients.ShouldBe(1);
        status.AlertEvaluationMilliseconds.ShouldBe(12);
        status.UptimeSeconds.ShouldBe(90);
        status.Healthy.ShouldBeTrue();
    }

    [Fact]
    public void Read_has_no_evaluation_time_before_the_first_run()
    {
        // Arrange
        var telemetry = new SystemTelemetry(TwoScopes(), new FakeAgentLiveness(), new FakeTimeProvider());

        // Act
        var status = telemetry.Read();

        // Assert
        status.AlertEvaluationMilliseconds.ShouldBeNull();
    }

    [Fact]
    public async Task System_answers_with_the_state_of_the_replica_as_json()
    {
        // Arrange
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IScopeRegistry>(TwoScopes());
        builder.Services.AddSingleton<IAgentLiveness>(new FakeAgentLiveness());
        builder.Services.AddSelfMonitoring();
        await using var app = builder.Build();
        app.MapSelfMonitoring();
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();

        // Act
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/system", Ct));

        // Assert
        json.RootElement.GetProperty("scopes").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("agentsConnected").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("healthy").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Read_puts_the_replicas_together_from_what_the_agents_say_and_is_not_healthy_while_one_is_not_connected()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", ReplicaRuleTests.Agent("a-1", ("c0:9440", true, 2), ("c1:9440", true, 7)))
            .WithInstance("blog", ReplicaRuleTests.Agent("a-2", ("c0:9440", true, 11), ("c1:9440", false, null)));
        var telemetry = new SystemTelemetry(registry, new FakeAgentLiveness(), new FakeTimeProvider());

        // Act
        var status = telemetry.Read();

        // Assert
        status.Replicas.Select(r => (r.Address, r.AgentsReporting, r.AgentsConnected, r.MaxLag)).ShouldBe(
            [("c0:9440", 2, 2, 11L), ("c1:9440", 2, 1, 7L)]);
        status.Healthy.ShouldBeFalse();
    }

    [Fact]
    public void Read_knows_which_replica_it_is_from_the_machine_name()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", ReplicaRuleTests.Agent("a-1",
            ($"{Environment.MachineName}.plover-headless:9440", true, 0), ("other.plover-headless:9440", true, 0)));

        // Act
        var status = new SystemTelemetry(registry, new FakeAgentLiveness(), new FakeTimeProvider()).Read();

        // Assert
        status.Replicas.Single(r => r.ThisReplica).Address.ShouldStartWith(Environment.MachineName);
        status.Replicas.Count(r => r.ThisReplica).ShouldBe(1);
    }

    [Fact]
    public void Read_lists_the_pods_that_run_without_a_sidecar_but_not_the_agent_or_those_that_have_one()
    {
        // Arrange
        static InstanceInfo Pod(string id, string service, params ContainerInfo[] containers) =>
            new(id, service, InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), Containers: containers);
        var app = new ContainerInfo("app", ContainerRole.App, ContainerPhase.Running, HealthState.Healthy, true);
        var sidecar = new ContainerInfo("plover-sidecar", ContainerRole.PloverSidecar, ContainerPhase.Running, HealthState.Healthy, true);
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Pod("orders-1", "orders", app))
            .WithInstance("shop", Pod("billing-1", "billing", app, sidecar))
            .WithInstance("shop", Pod("agent-1", "plover-agent", app))
            .WithInstance("shop", new InstanceInfo("legacy", "legacy", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));

        // Act
        var status = new SystemTelemetry(registry, new FakeAgentLiveness(), new FakeTimeProvider()).Read();

        // Assert
        status.Unmonitored.ShouldBe([new UnmonitoredInstance("shop", "orders", "orders-1")]);
    }
}
