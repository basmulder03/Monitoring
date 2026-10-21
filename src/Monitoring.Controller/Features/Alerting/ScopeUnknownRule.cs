using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for a scope whose agent is gone, because then nothing can be said about the scope's workloads.</summary>
/// <remarks>
/// The scope shows as Unknown (grey) at once; this alert escalates it when the agent stays away, so a dead agent
/// cannot go unnoticed behind a quiet tile. It does not mean the workloads are down.
/// </remarks>
/// <param name="registry">Source of the known scopes.</param>
/// <param name="liveness">Tells whether the agent of a scope is alive.</param>
/// <param name="options">Provides the wait time.</param>
internal sealed class ScopeUnknownRule(IScopeRegistry registry, IAgentLiveness liveness, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => "scope-unknown";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Critical;

    /// <inheritdoc />
    public TimeSpan Wait => options.ScopeUnknownWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects() =>
        registry.GetScopeNames().Where(scope => !liveness.GetScopeLiveness(scope).IsLive);
}
