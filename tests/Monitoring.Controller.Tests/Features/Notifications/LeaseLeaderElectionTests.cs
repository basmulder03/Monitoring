using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Notifications;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Notifications;

public class LeaseLeaderElectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);

    private sealed class FakeLease(FakeTimeProvider clock) : ILeaseApi
    {
        public KubernetesLease? Lease { get; set; }

        public bool Unreachable { get; set; }

        public int Version { get; private set; }

        public Task<KubernetesLease?> GetAsync(CancellationToken ct) => Unreachable ? throw new HttpRequestException("down") : Task.FromResult(Lease);

        public Task<bool> WriteAsync(KubernetesLease lease, CancellationToken ct)
        {
            if (Unreachable)
            {
                throw new HttpRequestException("down");
            }

            // Like the API server: an update only works on the version that was read.
            if ((Lease?.Metadata.ResourceVersion) != lease.Metadata.ResourceVersion)
            {
                return Task.FromResult(false);
            }

            Lease = lease with { Metadata = lease.Metadata with { ResourceVersion = (++Version).ToString(System.Globalization.CultureInfo.InvariantCulture) } };
            return Task.FromResult(true);
        }

        public void HeldBy(string holder, TimeSpan renewedAgo) =>
            Lease = new KubernetesLease(new LeaseMetadata("lease", "9"), new LeaseSpec(holder, 15, null, (clock.GetUtcNow() - renewedAgo).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static (LeaseLeaderElection Election, FakeLease Api, FakeTimeProvider Clock) Make(string identity = "replica-0")
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
        var api = new FakeLease(clock);
        return (new LeaseLeaderElection(api, "lease", identity, Duration, clock, NullLogger<LeaseLeaderElection>.Instance), api, clock);
    }

    [Fact]
    public async Task The_first_replica_creates_the_lease_and_leads()
    {
        var (election, api, _) = Make();

        await election.ElectAsync(Ct);

        election.IsLeader.ShouldBeTrue();
        api.Lease!.Spec.HolderIdentity.ShouldBe("replica-0");
    }

    [Fact]
    public async Task A_replica_does_not_take_a_lease_that_another_one_renewed_recently()
    {
        var (election, api, _) = Make();
        api.HeldBy("replica-1", renewedAgo: TimeSpan.FromSeconds(5));

        await election.ElectAsync(Ct);

        election.IsLeader.ShouldBeFalse();
        api.Lease!.Spec.HolderIdentity.ShouldBe("replica-1");
    }

    [Fact]
    public async Task A_replica_takes_over_a_lease_whose_holder_has_been_silent_for_its_whole_duration()
    {
        var (election, api, _) = Make();
        api.HeldBy("replica-1", renewedAgo: TimeSpan.FromSeconds(20));

        await election.ElectAsync(Ct);

        election.IsLeader.ShouldBeTrue();
        api.Lease!.Spec.HolderIdentity.ShouldBe("replica-0");
    }

    [Fact]
    public async Task The_leader_renews_and_keeps_leading_but_steps_down_when_it_cannot_renew_for_the_whole_duration()
    {
        var (election, api, clock) = Make();
        await election.ElectAsync(Ct);
        clock.Advance(TimeSpan.FromSeconds(5));
        await election.ElectAsync(Ct);
        election.IsLeader.ShouldBeTrue();

        api.Unreachable = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        await election.ElectAsync(Ct);
        election.IsLeader.ShouldBeTrue("its own lease has not run out yet");

        clock.Advance(TimeSpan.FromSeconds(6));
        await election.ElectAsync(Ct);
        election.IsLeader.ShouldBeFalse();
    }

    [Fact]
    public async Task Two_replicas_that_try_to_take_the_same_lease_at_once_do_not_both_win()
    {
        var (first, api, clock) = Make("replica-0");
        var second = new LeaseLeaderElection(api, "lease", "replica-1", Duration, clock, NullLogger<LeaseLeaderElection>.Instance);
        api.HeldBy("replica-2", renewedAgo: TimeSpan.FromSeconds(30));

        await first.ElectAsync(Ct);
        await second.ElectAsync(Ct);

        (first.IsLeader, second.IsLeader).ShouldBe((true, false));
    }
}
