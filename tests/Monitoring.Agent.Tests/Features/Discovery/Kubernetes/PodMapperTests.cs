using Monitoring.Agent.Features.Discovery.Kubernetes;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Agent.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class PodMapperTests
{
    [Fact]
    public void Map_uses_the_pod_name_as_the_instance_id()
    {
        // Arrange
        var pod = new PodBuilder().Named("orders-7d9f-abc12").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Id.ShouldBe("orders-7d9f-abc12");
    }

    [Fact]
    public void Map_ignores_a_pod_that_completed_successfully()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Succeeded").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance.ShouldBeNull();
    }

    [Theory]
    [InlineData("app.kubernetes.io/name", "orders", "orders")]
    [InlineData("app", "legacy-orders", "legacy-orders")]
    public void Map_names_the_service_after_the_name_label(string label, string value, string expected)
    {
        // Arrange
        var pod = new PodBuilder().Named("pod-x").WithLabel(label, value).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.ServiceName.ShouldBe(expected);
    }

    [Fact]
    public void Map_prefers_the_standard_name_label_over_the_app_label()
    {
        // Arrange
        var pod = new PodBuilder().WithLabel("app", "old").WithLabel("app.kubernetes.io/name", "new").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.ServiceName.ShouldBe("new");
    }

    [Theory]
    [InlineData("ReplicaSet", "orders-7d9f6c5b8", "orders")]
    [InlineData("ReplicaSet", "order-service-5fd8", "order-service")]
    [InlineData("ReplicaSet", "nodash", "nodash")]
    [InlineData("StatefulSet", "kafka-broker", "kafka-broker")]
    [InlineData("DaemonSet", "log-agent", "log-agent")]
    [InlineData("Job", "nightly-1234", "nightly-1234")]
    public void Map_names_the_service_after_the_owning_workload_when_there_is_no_label(string kind, string owner, string expected)
    {
        // Arrange
        var pod = new PodBuilder().Named("pod-x").OwnedBy(kind, owner).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.ServiceName.ShouldBe(expected);
    }

    [Fact]
    public void Map_prefers_the_managing_controller_among_several_owners()
    {
        // Arrange
        var pod = new PodBuilder().OwnedBy("Other", "helper", controller: false).OwnedBy("StatefulSet", "db", controller: true).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.ServiceName.ShouldBe("db");
    }

    [Fact]
    public void Map_names_a_standalone_pod_service_after_the_pod()
    {
        // Arrange
        var pod = new PodBuilder().Named("debug-shell").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.ServiceName.ShouldBe("debug-shell");
    }

    [Theory]
    [InlineData("Pending", false, InstanceLifecycle.Starting)]
    [InlineData("Running", false, InstanceLifecycle.Running)]
    [InlineData("Unknown", false, InstanceLifecycle.Running)]
    [InlineData("Failed", false, InstanceLifecycle.Terminating)]
    [InlineData("Running", true, InstanceLifecycle.Terminating)]
    [InlineData("Pending", true, InstanceLifecycle.Terminating)]
    public void Map_derives_the_lifecycle_from_the_phase_and_deletion(string phase, bool deleting, InstanceLifecycle expected)
    {
        // Arrange
        var builder = new PodBuilder().InPhase(phase);
        var pod = (deleting ? builder.Deleting() : builder).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Lifecycle.ShouldBe(expected);
    }

    [Fact]
    public void Map_reports_a_running_pod_with_ready_containers_as_healthy()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").PodReady().WithContainer("app", ready: true).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public void Map_reports_a_running_pod_that_is_not_ready_as_degraded()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").WithContainer("app", ready: false).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Degraded);
    }

    [Fact]
    public void Map_reports_a_running_pod_with_one_unready_container_as_degraded()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").PodReady()
            .WithContainer("app", ready: true).WithContainer("sidecar", ready: false).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Degraded);
    }

    [Theory]
    [InlineData("CrashLoopBackOff")]
    [InlineData("ImagePullBackOff")]
    [InlineData("ErrImagePull")]
    [InlineData("CreateContainerConfigError")]
    [InlineData("InvalidImageName")]
    public void Map_reports_a_container_that_is_stuck_as_unhealthy(string reason)
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").WithContainer("app", ready: false, waitingReason: reason).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unhealthy);
    }

    [Theory]
    [InlineData("ContainerCreating")]
    [InlineData("PodInitializing")]
    public void Map_does_not_treat_a_container_that_is_starting_normally_as_failing(string reason)
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Pending").WithContainer("app", ready: false, waitingReason: reason).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unknown);
    }

    [Theory]
    [InlineData(137)]
    [InlineData(1)]
    public void Map_reports_a_container_that_exited_with_an_error_as_unhealthy(int exitCode)
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").WithContainer("app", ready: false, terminatedExitCode: exitCode).Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public void Map_reports_a_failed_pod_as_unhealthy()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Failed").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public void Map_reports_a_pod_in_an_unknown_phase_as_unknown()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Unknown").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unknown);
    }

    [Fact]
    public void Map_reports_a_pending_pod_without_container_statuses_as_unknown()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Pending").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Unknown);
    }

    [Fact]
    public void Map_keeps_the_health_of_a_terminating_pod()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Running").PodReady().WithContainer("app", ready: true).Deleting().Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public void Map_carries_the_pod_labels_node_and_address()
    {
        // Arrange
        var pod = new PodBuilder().WithLabel("team", "payments").OnNode("node-3").WithIp("10.1.2.3").Build();

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Labels.ShouldBe(new Dictionary<string, string>
        {
            ["team"] = "payments",
            ["k8s.node"] = "node-3",
            ["k8s.pod-ip"] = "10.1.2.3",
            ["k8s.pod-ips"] = "10.1.2.3",
        });
    }

    [Fact]
    public void Map_lists_every_address_of_a_dual_stack_pod_once()
    {
        // Arrange
        var pod = new PodBuilder().WithIp("10.1.2.3").Build();
        pod = pod with { Status = pod.Status! with { PodIPs = [new PodIPAddress("10.1.2.3"), new PodIPAddress("fd00::3")] } };

        // Act
        var instance = PodMapper.Map(pod);

        // Assert
        instance!.Labels["k8s.pod-ips"].ShouldBe("10.1.2.3,fd00::3");
        instance.Labels["k8s.pod-ip"].ShouldBe("10.1.2.3");
    }

    [Fact]
    public void Map_does_not_change_the_labels_of_the_pod_it_was_given()
    {
        // Arrange
        var pod = new PodBuilder().WithLabel("team", "payments").OnNode("node-3").Build();

        // Act
        PodMapper.Map(pod);

        // Assert
        pod.Metadata.Labels.ShouldBe(new Dictionary<string, string> { ["team"] = "payments" });
    }

    [Fact]
    public void Map_lists_the_application_and_its_sidecars_with_their_roles()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithRunningContainer("orders", ready: true)
            .WithRunningContainer("monitoring-sidecar", ready: true)
            .WithRunningContainer("proxy", ready: true)
            .PodReady()
            .Build();

        // Act
        var containers = PodMapper.Map(pod)!.Containers!;

        // Assert
        containers.Select(c => (c.Name, c.Role)).ShouldBe(
            [("orders", ContainerRole.App), ("monitoring-sidecar", ContainerRole.MonitoringSidecar), ("proxy", ContainerRole.OtherSidecar)]);
    }

    [Fact]
    public void Map_reports_the_state_of_each_container()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithRunningContainer("orders", ready: false, restarts: 3, lastReason: "OOMKilled", lastExitCode: 137, digest: "sha256:abc")
            .Build();

        // Act
        var container = PodMapper.Map(pod)!.Containers!.Single();

        // Assert
        container.Phase.ShouldBe(ContainerPhase.Running);
        container.Health.ShouldBe(HealthState.Degraded);
        container.Restarts.ShouldBe(3);
        container.LastReason.ShouldBe("OOMKilled");
        container.LastExitCode.ShouldBe(137);
        container.Image.ShouldBe("orders:1");
        container.ImageDigest.ShouldBe("sha256:abc");
    }

    [Fact]
    public void Map_gives_a_waiting_container_its_reason_and_message()
    {
        // Arrange
        var pod = new PodBuilder().WithWaitingContainer("orders", "ImagePullBackOff", "no such image").Build();

        // Act
        var container = PodMapper.Map(pod)!.Containers!.Single();

        // Assert
        container.Phase.ShouldBe(ContainerPhase.Waiting);
        container.Health.ShouldBe(HealthState.Unhealthy);
        container.Reason.ShouldBe("ImagePullBackOff");
        container.Message.ShouldBe("no such image");
    }

    [Fact]
    public void Map_shows_a_container_that_is_still_being_created_as_unknown()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Pending").WithWaitingContainer("orders", "ContainerCreating").Build();

        // Act
        var container = PodMapper.Map(pod)!.Containers!.Single();

        // Assert
        container.Health.ShouldBe(HealthState.Unknown);
    }

    [Fact]
    public void Map_lists_the_containers_of_a_pod_that_has_no_status_yet_as_waiting()
    {
        // Arrange
        var pod = new PodBuilder().InPhase("Pending").Build();

        // Act
        var container = PodMapper.Map(pod)!.Containers!.Single();

        // Assert
        container.Name.ShouldBe("app");
        container.Phase.ShouldBe(ContainerPhase.Waiting);
    }

    [Fact]
    public void Map_shows_an_init_container_only_while_it_has_not_finished_well()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithInitContainer("migrate", terminatedExitCode: 0)
            .WithInitContainer("wait-for-db")
            .WithInitContainer("broken", terminatedExitCode: 1)
            .WithRunningContainer("orders", ready: true)
            .Build();

        // Act
        var containers = PodMapper.Map(pod)!.Containers!;

        // Assert
        containers.Where(c => c.Role == ContainerRole.Init).Select(c => c.Name).ShouldBe(["wait-for-db", "broken"]);
    }

    [Fact]
    public void Map_treats_an_init_container_that_keeps_running_as_a_sidecar()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithInitContainer("monitoring-sidecar", nativeSidecar: true)
            .WithInitContainer("proxy", nativeSidecar: true)
            .WithRunningContainer("orders", ready: true)
            .Build();

        // Act
        var containers = PodMapper.Map(pod)!.Containers!;

        // Assert
        containers.Single(c => c.Name == "monitoring-sidecar").Role.ShouldBe(ContainerRole.MonitoringSidecar);
        containers.Single(c => c.Name == "proxy").Role.ShouldBe(ContainerRole.OtherSidecar);
    }

    [Fact]
    public void Map_shows_a_failing_monitoring_sidecar_as_degraded_instead_of_unhealthy()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithRunningContainer("orders", ready: true)
            .WithWaitingContainer("monitoring-sidecar", "CrashLoopBackOff")
            .Build();

        // Act
        var instance = PodMapper.Map(pod)!;

        // Assert
        instance.Health.ShouldBe(HealthState.Degraded);
        instance.Checks!.Single().Name.ShouldBe("monitoring sidecar");
        instance.Checks!.Single().Description.ShouldBe("CrashLoopBackOff");
    }

    [Fact]
    public void Map_still_shows_a_failing_application_as_unhealthy_next_to_a_good_sidecar()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithWaitingContainer("orders", "CrashLoopBackOff")
            .WithRunningContainer("monitoring-sidecar", ready: true)
            .Build();

        // Act
        var instance = PodMapper.Map(pod)!;

        // Assert
        instance.Health.ShouldBe(HealthState.Unhealthy);
        instance.Checks.ShouldBeNull();
    }

    [Fact]
    public void Map_uses_the_configured_name_of_the_monitoring_sidecar()
    {
        // Arrange
        var pod = new PodBuilder()
            .WithRunningContainer("orders", ready: true)
            .WithWaitingContainer("mon", "CrashLoopBackOff")
            .Build();

        // Act
        var instance = PodMapper.Map(pod, "mon")!;

        // Assert
        instance.Health.ShouldBe(HealthState.Degraded);
        instance.Containers!.Single(c => c.Name == "mon").Role.ShouldBe(ContainerRole.MonitoringSidecar);
    }
}
