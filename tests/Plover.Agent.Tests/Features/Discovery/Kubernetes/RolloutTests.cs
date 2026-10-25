using Plover.Agent.Features.Discovery.Kubernetes;
using Plover.Agent.Features.Discovery.Kubernetes.Models;
using Plover.Agent.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.Discovery.Kubernetes;

public class RolloutTests
{
    private static KubernetesWorkload Workload(int? desired, int? ready, int? updated, long? generation = null, long? observed = null, int? total = null) =>
        new(new WorkloadMetadata("orders", generation), new WorkloadSpec(desired), new WorkloadStatus(observed, ready, updated, total));

    [Theory]
    [InlineData(3, 3, 3, null)]
    [InlineData(3, 2, 3, "2 of 3 replicas are ready.")]
    [InlineData(3, 2, 1, "Rolling out: 1 of 3 replicas updated, 2 ready.")]
    [InlineData(3, null, null, "Rolling out: 0 of 3 replicas updated, 0 ready.")]
    [InlineData(0, 0, 0, null)]
    public void Describe_says_what_is_going_on_with_a_workload(int desired, int? ready, int? updated, string? expected)
    {
        // Act
        var text = Rollout.Describe(Workload(desired, ready, updated));

        // Assert
        text.ShouldBe(expected);
    }

    [Fact]
    public void A_new_pod_that_never_becomes_ready_is_a_rollout_that_is_stuck_even_though_one_pod_is_ready_and_one_is_updated()
    {
        // Act: the new pod counts as updated, and the old one still serves
        var text = Rollout.Describe(Workload(1, 1, 1, total: 2));

        // Assert
        text.ShouldBe("Rolling out: 2 replicas exist for 1 wanted, 1 ready.");
    }

    [Fact]
    public void A_workload_that_does_not_say_how_many_it_wants_wants_one()
    {
        // Act
        var text = Rollout.Describe(Workload(null, 0, 0));

        // Assert
        text.ShouldBe("Rolling out: 0 of 1 replicas updated, 0 ready.");
    }

    [Fact]
    public void A_change_that_its_controller_has_not_acted_on_yet_is_a_rollout_even_when_all_pods_are_ready()
    {
        // Act
        var text = Rollout.Describe(Workload(3, 3, 3, generation: 5, observed: 4));

        // Assert
        text.ShouldBe("A change is being rolled out.");
    }

    [Fact]
    public void KeyOf_finds_the_deployment_through_the_replica_set_and_the_statefulset_directly()
    {
        // Arrange
        var deployed = new PodBuilder().Named("p1").OwnedBy("ReplicaSet", "orders-api-7d9f8b6c5d").Build();
        var stateful = new PodBuilder().Named("db-0").OwnedBy("StatefulSet", "db").Build();
        var job = new PodBuilder().Named("p2").OwnedBy("Job", "migrate-1").Build();
        var bare = new PodBuilder().Named("p3").Build();

        // Assert
        Rollout.KeyOf(deployed).ShouldBe("Deployment/orders-api");
        Rollout.KeyOf(stateful).ShouldBe("StatefulSet/db");
        Rollout.KeyOf(job).ShouldBeNull();
        Rollout.KeyOf(bare).ShouldBeNull();
    }
}
