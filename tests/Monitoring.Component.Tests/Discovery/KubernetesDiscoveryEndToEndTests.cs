using Monitoring.Component.Tests.Support;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Discovery;

/// <summary>The pods of a (fake) Kubernetes cluster arriving at a real controller through a real agent.</summary>
public class KubernetesDiscoveryEndToEndTests
{
    private const string Token = "enroll";

    [Fact]
    public async Task Pods_of_the_cluster_show_up_on_the_controller_under_the_service_they_belong_to()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").WithPod("orders-2").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();

        // Assert
        await Eventually.AssertAsync(() =>
        {
            controller.InstanceIds("shop").ShouldBe(["orders-1", "orders-2"]);
            controller.Registry.GetInstances("shop").ShouldAllBe(i => i.ServiceName == "orders" && i.Health == HealthState.Healthy);
        });
    }

    [Fact]
    public async Task The_agent_authenticates_to_the_api_server_with_its_token()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["orders-1"]));

        // Assert
        cluster.LastAuthorization.ShouldBe("Bearer cluster-token");
    }

    [Fact]
    public async Task A_pod_that_appears_later_is_reported_without_listing_again()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await Eventually.AssertAsync(() => cluster.OpenWatches.ShouldBe(1));

        // Act
        cluster.AddPod("orders-2");

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["orders-1", "orders-2"]));
    }

    [Fact]
    public async Task A_pod_that_stops_being_ready_turns_degraded_on_the_controller()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await Eventually.AssertAsync(() => cluster.OpenWatches.ShouldBe(1));

        // Act
        cluster.ChangePod("orders-1", ready: false);

        // Assert
        await Eventually.AssertAsync(() => controller.Registry.GetInstances("shop").Single().Health.ShouldBe(HealthState.Degraded));
    }

    [Fact]
    public async Task A_deleted_pod_is_shown_as_gone_on_the_controller()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").WithPod("orders-2").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        await Eventually.AssertAsync(() => cluster.OpenWatches.ShouldBe(1));

        // Act
        cluster.DeletePod("orders-1");

        // Assert
        await Eventually.AssertAsync(() =>
            controller.Registry.GetInstances("shop").Single(i => i.Id == "orders-1").Lifecycle.ShouldBe(InstanceLifecycle.Gone));
    }
}
