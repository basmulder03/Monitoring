namespace Monitoring.Controller.Features.Alerting;

/// <summary>What this replica knows of the other replicas, which is what picks the one that sends alerts when the replicas do not share a Kubernetes cluster.</summary>
public interface IReplicaPeers
{
    /// <summary>
    /// Whether no replica that ranks before this one can be reached right now. A replica that cannot be reached counts as gone, so when the link between two data centers
    /// breaks both sides send; an alert is then told twice rather than not at all.
    /// </summary>
    bool IsFirst { get; }
}

/// <summary>For a replica that has no peers: it ranks first.</summary>
internal sealed class NoPeers : IReplicaPeers
{
    /// <inheritdoc />
    public bool IsFirst => true;
}
