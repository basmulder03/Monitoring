using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.Discovery.Kubernetes;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class KubernetesLeaderElectionTests
{
    private static readonly LeaderElectionOptions Options = new() { LeaseDuration = TimeSpan.FromSeconds(15), RenewInterval = TimeSpan.FromSeconds(5) };

    [Fact]
    public async Task The_first_agent_creates_the_lease_and_serves()
    {
        // Arrange
        var world = new World();
        var a = world.Agent("a");

        // Act
        await a.StepAsync(TestContext.Current.CancellationToken);

        // Assert
        a.IsLeader.ShouldBeTrue();
        world.Api.Lease!.Spec.HolderIdentity.ShouldBe("a");
    }

    [Fact]
    public async Task A_second_agent_waits_while_the_holder_keeps_renewing()
    {
        // Arrange
        var world = new World();
        var a = world.Agent("a");
        var b = world.Agent("b");
        await a.StepAsync(TestContext.Current.CancellationToken);

        // Act
        for (var i = 0; i < 6; i++)
        {
            world.Time.Advance(TimeSpan.FromSeconds(5));
            await a.StepAsync(TestContext.Current.CancellationToken);
            await b.StepAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        b.IsLeader.ShouldBeFalse();
        world.Api.Lease!.Spec.HolderIdentity.ShouldBe("a");
    }

    [Fact]
    public async Task A_waiting_agent_takes_over_once_the_lease_has_not_changed_for_its_duration()
    {
        // Arrange
        var world = new World();
        var a = world.Agent("a");
        var b = world.Agent("b");
        await a.StepAsync(TestContext.Current.CancellationToken);
        await b.StepAsync(TestContext.Current.CancellationToken);

        // Act: a stops renewing
        world.Time.Advance(TimeSpan.FromSeconds(10));
        await b.StepAsync(TestContext.Current.CancellationToken);
        var early = b.IsLeader;
        world.Time.Advance(TimeSpan.FromSeconds(6));
        await b.StepAsync(TestContext.Current.CancellationToken);

        // Assert
        early.ShouldBeFalse();
        b.IsLeader.ShouldBeTrue();
        world.Api.Lease!.Spec.HolderIdentity.ShouldBe("b");
        world.Api.Lease.Spec.LeaseTransitions.ShouldBe(1);
    }

    [Fact]
    public async Task An_agent_that_loses_the_race_for_an_expired_lease_does_not_serve()
    {
        // Arrange
        var world = new World();
        world.Api.Lease = new Lease(new LeaseMetadata("monitoring-agent", "1"), new LeaseSpec("old", 15, "t0", "t0", 0));
        var a = world.Agent("a");
        await a.StepAsync(TestContext.Current.CancellationToken);
        world.Time.Advance(TimeSpan.FromSeconds(16));
        world.Api.RaceOnNextWrite = () => world.Api.Lease = new Lease(new LeaseMetadata("monitoring-agent", "2"), new LeaseSpec("b", 15, "t1", "t1", 1));

        // Act
        await a.StepAsync(TestContext.Current.CancellationToken);

        // Assert
        a.IsLeader.ShouldBeFalse();
        world.Api.Lease!.Spec.HolderIdentity.ShouldBe("b");
    }

    [Fact]
    public async Task A_holder_that_cannot_renew_for_the_lease_duration_stops_the_agent()
    {
        // Arrange
        var world = new World();
        var a = world.Agent("a");
        await a.StepAsync(TestContext.Current.CancellationToken);
        world.Api.Fail = true;

        // Act
        world.Time.Advance(TimeSpan.FromSeconds(10));
        await a.StepAsync(TestContext.Current.CancellationToken);
        var early = world.Lifetime.Stopped;
        world.Time.Advance(TimeSpan.FromSeconds(6));
        await a.StepAsync(TestContext.Current.CancellationToken);

        // Assert
        early.ShouldBeFalse();
        world.Lifetime.Stopped.ShouldBeTrue();
    }

    [Fact]
    public async Task A_stopping_holder_gives_the_lease_up_so_the_next_agent_takes_it_at_once()
    {
        // Arrange
        var world = new World();
        var a = world.Agent("a");
        var b = world.Agent("b");
        await a.StepAsync(TestContext.Current.CancellationToken);
        await b.StepAsync(TestContext.Current.CancellationToken);

        // Act
        await a.StopAsync(TestContext.Current.CancellationToken);
        await b.StepAsync(TestContext.Current.CancellationToken);

        // Assert
        b.IsLeader.ShouldBeTrue();
    }

    private sealed class World
    {
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

        public FakeLeaseApi Api { get; } = new();

        public FakeLifetime Lifetime { get; } = new();

        public KubernetesLeaderElection Agent(string identity) =>
            new(Api, new DiscoveryTarget("ns"), Options, identity, Time, Lifetime, NullLogger<KubernetesLeaderElection>.Instance);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public bool Stopped { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Stopped = true;
    }

    /// <summary>A lease store with the version check of the API server.</summary>
    private sealed class FakeLeaseApi : ILeaseApi
    {
        private int _version;

        public Lease? Lease { get; set; }

        public bool Fail { get; set; }

        public Action? RaceOnNextWrite { get; set; }

        public Task<Lease?> GetLeaseAsync(string namespaceName, string name, CancellationToken ct) =>
            Fail ? throw new KubernetesApiException("down") : Task.FromResult(Lease);

        public Task<Lease> CreateLeaseAsync(string namespaceName, Lease lease, CancellationToken ct) =>
            Lease is not null ? throw new KubernetesApiException("exists", 409) : Task.FromResult(Store(lease));

        public Task<Lease> ReplaceLeaseAsync(string namespaceName, Lease lease, CancellationToken ct)
        {
            if (Fail)
            {
                throw new KubernetesApiException("down");
            }

            if (RaceOnNextWrite is { } race)
            {
                RaceOnNextWrite = null;
                race();
            }

            return lease.Metadata.ResourceVersion != Lease?.Metadata.ResourceVersion
                ? throw new KubernetesApiException("conflict", 409)
                : Task.FromResult(Store(lease));
        }

        private Lease Store(Lease lease) => Lease = lease with { Metadata = lease.Metadata with { ResourceVersion = (++_version).ToString() } };
    }
}
