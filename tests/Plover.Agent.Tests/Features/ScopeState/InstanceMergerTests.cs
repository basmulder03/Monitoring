using Plover.Agent.Features.ScopeState;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.ScopeState;

public class InstanceMergerTests
{
    private static InstanceInfo Pod(HealthState health = HealthState.Healthy, InstanceLifecycle lifecycle = InstanceLifecycle.Running) =>
        new("orders-1", "orders-from-pod", lifecycle, health, new Dictionary<string, string> { ["k8s.node"] = "node-1" });

    private static ApplicationReport App(HealthState health = HealthState.Healthy, string service = "orders", string? version = "1.2.3", params HealthCheckResult[] checks) =>
        new(service, version, health, checks);

    [Fact]
    public void Merge_leaves_the_instance_as_it_is_when_the_application_never_reported()
    {
        // Arrange
        var pod = Pod();

        // Act
        var shown = InstanceMerger.Merge(pod, application: null, sidecarLost: false);

        // Assert
        shown.ShouldBeSameAs(pod);
    }

    [Fact]
    public void Merge_keeps_the_checks_of_the_platform_beside_those_of_the_application()
    {
        // Arrange
        var rollout = new HealthCheckResult("rollout", HealthState.Degraded, "Rolling out: 1 of 3 replicas updated, 2 ready.");
        var database = new HealthCheckResult("database", HealthState.Healthy, null);
        var pod = Pod() with { Checks = [rollout] };

        // Act
        var shown = InstanceMerger.Merge(pod, App(HealthState.Healthy, "orders", "1.2.3", database), sidecarLost: false);

        // Assert
        shown.Checks.ShouldBe([rollout, database]);
    }

    [Fact]
    public void Merge_keeps_the_use_of_the_pod_beside_the_metrics_of_the_application()
    {
        // Arrange
        var usage = new MetricSeries("k8s.pod.cpu_usage_cores", new Dictionary<string, string>(), 0.2);
        var own = new MetricSeries("http.requests", new Dictionary<string, string>(), 7);

        // Act
        var shown = InstanceMerger.Merge(Pod() with { Metrics = [usage] }, App() with { Metrics = [own] }, sidecarLost: false);

        // Assert
        shown.Metrics.ShouldBe([usage, own]);
    }

    [Fact]
    public void Merge_takes_the_service_name_and_version_from_the_application()
    {
        // Arrange
        var pod = Pod();

        // Act
        var shown = InstanceMerger.Merge(pod, App(service: "orders", version: "1.2.3"), sidecarLost: false);

        // Assert
        shown.ServiceName.ShouldBe("orders");
        shown.Version.ShouldBe("1.2.3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Merge_keeps_the_service_name_of_the_platform_when_the_application_gives_none(string name)
    {
        // Arrange
        var pod = Pod();

        // Act
        var shown = InstanceMerger.Merge(pod, App(service: name), sidecarLost: false);

        // Assert
        shown.ServiceName.ShouldBe("orders-from-pod");
    }

    [Fact]
    public void Merge_keeps_what_the_platform_knows_about_the_instance()
    {
        // Arrange
        var pod = Pod(lifecycle: InstanceLifecycle.Terminating);

        // Act
        var shown = InstanceMerger.Merge(pod, App(), sidecarLost: false);

        // Assert
        shown.Id.ShouldBe("orders-1");
        shown.Lifecycle.ShouldBe(InstanceLifecycle.Terminating);
        shown.Labels["k8s.node"].ShouldBe("node-1");
    }

    [Theory]
    [InlineData(HealthState.Healthy, HealthState.Healthy, HealthState.Healthy)]
    [InlineData(HealthState.Healthy, HealthState.Degraded, HealthState.Degraded)]
    [InlineData(HealthState.Healthy, HealthState.Unhealthy, HealthState.Unhealthy)]
    [InlineData(HealthState.Degraded, HealthState.Healthy, HealthState.Degraded)]
    [InlineData(HealthState.Unhealthy, HealthState.Healthy, HealthState.Unhealthy)]
    [InlineData(HealthState.Unknown, HealthState.Healthy, HealthState.Unknown)]
    [InlineData(HealthState.Degraded, HealthState.Unhealthy, HealthState.Unhealthy)]
    public void Merge_shows_the_worse_of_what_the_platform_and_the_application_say(HealthState platform, HealthState application, HealthState expected)
    {
        // Arrange
        var pod = Pod(platform);

        // Act
        var shown = InstanceMerger.Merge(pod, App(application), sidecarLost: false);

        // Assert
        shown.Health.ShouldBe(expected);
    }

    [Fact]
    public void Merge_carries_the_health_checks_of_the_application()
    {
        // Arrange
        var check = new HealthCheckResult("database", HealthState.Unhealthy, "down");

        // Act
        var shown = InstanceMerger.Merge(Pod(), App(HealthState.Unhealthy, checks: check), sidecarLost: false);

        // Assert
        shown.Checks!.Single().ShouldBe(check);
    }

    [Fact]
    public void Merge_shows_an_instance_whose_sidecar_is_lost_as_unhealthy_and_says_why()
    {
        // Arrange
        var pod = Pod(HealthState.Healthy);

        // Act
        var shown = InstanceMerger.Merge(pod, App(HealthState.Healthy, version: "1.2.3"), sidecarLost: true);

        // Assert
        shown.Health.ShouldBe(HealthState.Unhealthy);
        shown.Checks!.Single().Name.ShouldBe("Plover sidecar");
        shown.Checks!.Single().Description.ShouldBe("The sidecar is not connected.");
    }

    [Fact]
    public void Merge_keeps_what_was_known_about_an_instance_whose_sidecar_is_lost()
    {
        // Arrange
        var pod = Pod();

        // Act
        var shown = InstanceMerger.Merge(pod, App(service: "orders", version: "1.2.3"), sidecarLost: true);

        // Assert
        shown.ServiceName.ShouldBe("orders");
        shown.Version.ShouldBe("1.2.3");
    }
}
