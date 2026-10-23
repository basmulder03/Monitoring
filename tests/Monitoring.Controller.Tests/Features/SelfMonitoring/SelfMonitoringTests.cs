using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.SelfMonitoring;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.SelfMonitoring;

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
}
