namespace Plover.Agent.Features.Discovery;

/// <summary>
/// Lets several agents run for one namespace: they compete for a Kubernetes Lease, and only the holder listens for sidecars and connects to the
/// controllers. The others wait, and one takes over when the holder stops. The role needs <c>get</c>, <c>create</c> and <c>update</c> on <c>leases</c>.
/// </summary>
public sealed class LeaderElectionOptions
{
    /// <summary>Off by default: a single agent needs no lease.</summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the Lease in the namespace of the agent.</summary>
    public string LeaseName { get; set; } = "plover-agent";

    /// <summary>How long after its last renewal a lease can be taken over. A holder that cannot renew for this long stops itself.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the holder renews and the others look.</summary>
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(5);
}
