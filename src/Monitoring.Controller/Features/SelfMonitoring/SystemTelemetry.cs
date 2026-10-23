using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;

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

    /// <inheritdoc />
    public void EventClientConnected() => Interlocked.Increment(ref _eventClients);

    /// <inheritdoc />
    public void EventClientDisconnected() => Interlocked.Decrement(ref _eventClients);

    /// <inheritdoc />
    public void AlertsEvaluated(TimeSpan took) => Interlocked.Exchange(ref _evaluationTicks, took.Ticks);

    /// <summary>Reads the state of this replica now.</summary>
    /// <returns>The state.</returns>
    public SystemStatus Read()
    {
        var scopes = registry.GetScopeNames();
        var connected = scopes.Count(scope => liveness.GetScopeLiveness(scope).IsLive);
        var ticks = Interlocked.Read(ref _evaluationTicks);
        return new SystemStatus(
            Environment.MachineName,
            (long)time.GetElapsedTime(_started).TotalSeconds,
            scopes.Count,
            connected,
            Volatile.Read(ref _eventClients),
            ticks < 0 ? null : TimeSpan.FromTicks(ticks).TotalMilliseconds,
            Environment.WorkingSet,
            connected == scopes.Count);
    }
}
