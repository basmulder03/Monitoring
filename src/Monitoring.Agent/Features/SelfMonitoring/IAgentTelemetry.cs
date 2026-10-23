namespace Monitoring.Agent.Features.SelfMonitoring;

/// <summary>What the other slices of the agent tell about its work, so the agent can report it about itself.</summary>
/// <remarks>All members are safe to call from several threads. The number of names is bounded by the callers, which use fixed ones.</remarks>
public interface IAgentTelemetry
{
    /// <summary>Adds to a value, for a counter that only grows or a gauge that goes up and down.</summary>
    /// <param name="name">The metric name, <c>monitoring.agent.*</c>.</param>
    /// <param name="delta">The amount to add.</param>
    void Add(string name, double delta = 1);

    /// <summary>Forgets everything that was measured about a replica, because the agent no longer connects to it.</summary>
    /// <param name="replica">The replica as <c>host:port</c>.</param>
    void Forget(string replica);

    /// <summary>Sets a value, for a gauge such as the duration of the latest pull.</summary>
    /// <param name="name">The metric name, <c>monitoring.agent.*</c>.</param>
    /// <param name="value">The value.</param>
    /// <param name="replica">The controller replica the value is about, as <c>host:port</c>; <see langword="null"/> for a value about the agent as a whole.</param>
    void Set(string name, double value, string? replica = null);
}
