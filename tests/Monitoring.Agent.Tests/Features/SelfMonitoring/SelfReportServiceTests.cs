using Monitoring.Protocol.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SelfMonitoring;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.SelfMonitoring;

public class SelfReportServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InstanceInfo Pod(string id) => new(id, "pod", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>());

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "shop" });
            Store.Upsert(Pod("agent-pod"));
            Store.Upsert(Pod("orders-1"));
            Store.Upsert(Pod("orders-2"));
            Service = new SelfReportService(Telemetry, Store, new SelfMonitoringOptions { PodName = "agent-pod", Interval = TimeSpan.FromSeconds(15) }, Clock, NullLogger<SelfReportService>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new();

        public AgentTelemetry Telemetry { get; } = new();

        public ScopeStateStore Store { get; }

        public SelfReportService Service { get; }

        public InstanceInfo AgentInstance => Store.CreateSnapshot().Instances.Single(instance => instance.Id == "agent-pod");

        public async Task WaitForMetricsAsync()
        {
            for (var i = 0; i < 200 && AgentInstance.Metrics is null; i++)
            {
                await Task.Delay(10, Ct);
            }
        }
    }

    [Fact]
    public void Read_counts_the_instances_watched_without_the_agent_itself()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        var series = fixture.Service.Read().ToDictionary(s => s.Name, s => s.Value);

        // Assert
        series["monitoring.agent.instances_watched"].ShouldBe(2);
        series["monitoring.agent.memory_bytes"].ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Read_says_how_far_each_connected_replica_is_behind_and_whether_it_is_connected()
    {
        // Arrange
        var fixture = new Fixture();
        foreach (var id in new[] { "x", "y", "z" })
        {
            fixture.Store.Upsert(Pod(id));
            fixture.Store.Publish();
        }

        var sequence = fixture.Store.CreateSnapshot().Sequence;
        fixture.Telemetry.Set(ReplicaMetrics.Connected, 1, "c1:9440");
        fixture.Telemetry.Set(ReplicaMetrics.AckedSequence, sequence - 2, "c1:9440");
        fixture.Telemetry.Set(ReplicaMetrics.Connected, 0, "c2:9440");
        fixture.Telemetry.Set(ReplicaMetrics.AckedSequence, 1, "c2:9440");

        // Act
        var series = fixture.Service.Read();

        // Assert
        double Value(string name, string replica) => series.Single(s => s.Name == name && s.Labels["replica"] == replica).Value;
        Value(ReplicaMetrics.Connected, "c1:9440").ShouldBe(1);
        Value(ReplicaMetrics.Lag, "c1:9440").ShouldBe(2);
        Value(ReplicaMetrics.Connected, "c2:9440").ShouldBe(0);
        series.ShouldNotContain(s => s.Name == ReplicaMetrics.Lag && s.Labels["replica"] == "c2:9440");
        series.ShouldNotContain(s => s.Name == ReplicaMetrics.AckedSequence);
    }

    [Fact]
    public void Read_includes_what_the_other_slices_counted()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Telemetry.Add("monitoring.agent.frames_sent");
        fixture.Telemetry.Add("monitoring.agent.frames_sent");
        fixture.Telemetry.Add("monitoring.agent.controller_connections");
        fixture.Telemetry.Add("monitoring.agent.controller_connections", -1);
        fixture.Telemetry.Set("monitoring.agent.pull_ms", 12.5);

        // Act
        var series = fixture.Service.Read().ToDictionary(s => s.Name, s => s.Value);

        // Assert
        series["monitoring.agent.frames_sent"].ShouldBe(2);
        series["monitoring.agent.controller_connections"].ShouldBe(0);
        series["monitoring.agent.pull_ms"].ShouldBe(12.5);
    }

    [Fact]
    public async Task The_service_reports_the_agent_as_the_application_of_its_own_pod()
    {
        // Arrange
        var fixture = new Fixture();

        // Act
        await fixture.Service.StartAsync(Ct);
        await fixture.WaitForMetricsAsync();
        await fixture.Service.StopAsync(Ct);

        // Assert
        var agent = fixture.AgentInstance;
        agent.ServiceName.ShouldBe("monitoring-agent");
        agent.Metrics!.Select(m => m.Name).ShouldContain("monitoring.agent.uptime_seconds");
    }

    [Fact]
    public void A_blank_pod_name_is_refused()
    {
        // Act
        var add = () => new ServiceCollection().AddSelfMonitoring(options => options.PodName = " ");

        // Assert
        add.ShouldThrow<ArgumentException>();
    }
}
