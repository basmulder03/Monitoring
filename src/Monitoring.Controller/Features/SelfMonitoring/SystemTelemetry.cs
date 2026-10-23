using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;

namespace Monitoring.Controller.Features.SelfMonitoring;

/// <summary>Default <see cref="ISystemTelemetry"/>, which also builds the <see cref="SystemStatus"/> from it and from the registry and liveness.</summary>
/// <param name="registry">Lists the scopes.</param>
/// <param name="liveness">Tells which scopes have a live agent.</param>
/// <param name="time">Clock for the uptime.</param>
internal sealed class SystemTelemetry(IScopeRegistry registry, IAgentLiveness liveness, TimeProvider time) : ISystemTelemetry
{
    private readonly long _started = time.GetTimestamp();
    private int _eventClients;
    private long _evaluationTicks = -1;
    private long _frames;

    private readonly Lock _rate = new();
    private long _sampledAt = time.GetTimestamp();
    private long _sampledFrames;
    private double _framesPerSecond;

    /// <summary>How long the frames are counted before the rate is worked out again.</summary>
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public void EventClientConnected() => Interlocked.Increment(ref _eventClients);

    /// <inheritdoc />
    public void EventClientDisconnected() => Interlocked.Decrement(ref _eventClients);

    /// <inheritdoc />
    public void AlertsEvaluated(TimeSpan took) => Interlocked.Exchange(ref _evaluationTicks, took.Ticks);

    /// <inheritdoc />
    public void FrameReceived() => Interlocked.Increment(ref _frames);

    /// <summary>The frames per second over the latest window of at least five seconds; every reader sees the same number, however often they ask.</summary>
    private double FramesPerSecond()
    {
        lock (_rate)
        {
            var elapsed = time.GetElapsedTime(_sampledAt);
            if (elapsed >= RateWindow)
            {
                var frames = Interlocked.Read(ref _frames);
                _framesPerSecond = (frames - _sampledFrames) / elapsed.TotalSeconds;
                _sampledAt = time.GetTimestamp();
                _sampledFrames = frames;
            }

            return _framesPerSecond;
        }
    }

    /// <summary>Reads the state of this replica now.</summary>
    /// <returns>The state.</returns>
    public SystemStatus Read()
    {
        var scopes = registry.GetScopeNames();
        var connected = scopes.Count(scope => liveness.GetScopeLiveness(scope).IsLive);
        var instances = scopes.SelectMany(scope => registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone).Select(i => (Scope: scope, Instance: i))).ToList();
        var replicas = ReplicasOf(instances);
        var ticks = Interlocked.Read(ref _evaluationTicks);
        return new SystemStatus(
            Environment.MachineName,
            (long)time.GetElapsedTime(_started).TotalSeconds,
            scopes.Count,
            connected,
            Volatile.Read(ref _eventClients),
            ticks < 0 ? null : TimeSpan.FromTicks(ticks).TotalMilliseconds,
            Environment.WorkingSet,
            connected == scopes.Count && replicas.All(replica => replica.AgentsConnected == replica.AgentsReporting),
            replicas,
            UnmonitoredOf(instances),
            FramesPerSecond(),
            instances.Count,
            instances.Sum(entry => (long)(entry.Instance.Metrics?.Count ?? 0)));
    }

    /// <summary>Every replica the agents connect to, put together from what each agent says about it.</summary>
    private static IReadOnlyList<ReplicaState> ReplicasOf(List<(string Scope, InstanceInfo Instance)> instances)
    {
        var reports = instances
            .Where(entry => entry.Instance.ServiceName == ReplicaMetrics.AgentService)
            .SelectMany(entry => ReplicaMetrics.From(entry.Instance.Metrics))
            .GroupBy(report => report.Replica, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        return
        [
            .. reports.Select(group => new ReplicaState(
                group.Key,
                IsThisReplica(group.Key),
                group.Count(),
                group.Count(report => report.Connected),
                group.Max(report => report.Lag ?? 0))),
        ];
    }

    /// <summary>A replica is this one when the host of its address starts with the name of the machine, as the pods of a stateful set do.</summary>
    private static bool IsThisReplica(string address) =>
        address.StartsWith(Environment.MachineName + ".", StringComparison.OrdinalIgnoreCase)
        || address.StartsWith(Environment.MachineName + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pods that run without the monitoring sidecar; the agent and the controller themselves have none and are left out.</summary>
    private static IReadOnlyList<UnmonitoredInstance> UnmonitoredOf(List<(string Scope, InstanceInfo Instance)> instances) =>
        [
            .. instances
                .Where(entry => entry.Instance.Containers is { Count: > 0 } containers
                    && entry.Instance.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running
                    && containers.All(container => container.Role != ContainerRole.MonitoringSidecar)
                    && entry.Instance.ServiceName is not (ReplicaMetrics.AgentService or "monitoring-controller"))
                .OrderBy(entry => entry.Scope, StringComparer.Ordinal).ThenBy(entry => entry.Instance.ServiceName, StringComparer.Ordinal).ThenBy(entry => entry.Instance.Id, StringComparer.Ordinal)
                .Select(entry => new UnmonitoredInstance(entry.Scope, entry.Instance.ServiceName, entry.Instance.Id)),
        ];
}
