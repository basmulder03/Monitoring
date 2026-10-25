namespace Plover.Controller.Features.SelfMonitoring;

/// <summary>The state of this controller replica, and of the replicas and agents it knows from what the agents report.</summary>
/// <param name="Replica">The host name of the replica.</param>
/// <param name="UptimeSeconds">How long the replica has been running.</param>
/// <param name="Scopes">How many scopes have reported.</param>
/// <param name="AgentsConnected">How many of those scopes have an agent that is alive.</param>
/// <param name="EventClients">How many pages have the live stream open.</param>
/// <param name="AlertEvaluationMilliseconds">How long the latest run of the alert rules took; <see langword="null"/> before the first.</param>
/// <param name="MemoryBytes">The memory in use by the process.</param>
/// <param name="Healthy">Whether every scope has a live agent and every agent is connected to every replica it knows.</param>
/// <param name="Replicas">Every controller replica the agents connect to, with how many agents are connected and how far behind the replica is.</param>
/// <param name="FramesPerSecond">How many frames the agents sent per second, over the last few seconds.</param>
/// <param name="Instances">How many instances this replica holds.</param>
/// <param name="Series">How many metric series those instances carry, which is what most of the memory goes to.</param>
/// <param name="Unmonitored">Pods that run without a Plover sidecar, so nothing is known of the application in them.</param>
public sealed record SystemStatus(
    string Replica,
    long UptimeSeconds,
    int Scopes,
    int AgentsConnected,
    int EventClients,
    double? AlertEvaluationMilliseconds,
    long MemoryBytes,
    bool Healthy,
    IReadOnlyList<ReplicaState> Replicas,
    IReadOnlyList<UnmonitoredInstance> Unmonitored,
    double FramesPerSecond,
    int Instances,
    long Series);

/// <summary>One controller replica, as the agents see it.</summary>
/// <param name="Address">The replica as <c>host:port</c>, as the agents connect to it.</param>
/// <param name="ThisReplica">Whether this is the replica that answers.</param>
/// <param name="AgentsReporting">How many agents say anything about the replica.</param>
/// <param name="AgentsConnected">How many of them are connected to it.</param>
/// <param name="MaxLag">The most changes of a scope that the replica has not acknowledged, for the agents connected to it.</param>
public sealed record ReplicaState(string Address, bool ThisReplica, int AgentsReporting, int AgentsConnected, long MaxLag);

/// <summary>A pod that runs without a Plover sidecar.</summary>
/// <param name="Scope">The scope of the pod.</param>
/// <param name="Service">The service it belongs to.</param>
/// <param name="Instance">The pod.</param>
public sealed record UnmonitoredInstance(string Scope, string Service, string Instance);
