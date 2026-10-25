using Plover.Controller.Features.Configuration;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Features.Registry;

namespace Plover.Controller.Features.Alerting;

/// <summary>Fires for a scope whose agent still uses another configuration than the controller expects, which usually means a change that did not arrive.</summary>
/// <remarks>A rollout passes through this state, so the wait is long. A scope without an agent is left to the alert for that, and one whose agent has not said which it uses is not in drift.</remarks>
/// <param name="registry">Source of the scopes and their labels.</param>
/// <param name="liveness">Tells whether the agent of a scope is alive.</param>
/// <param name="applied">Says which configuration each agent uses and which it should.</param>
/// <param name="options">Provides the wait.</param>
internal sealed class ConfigurationDriftRule(IScopeRegistry registry, IAgentLiveness liveness, IAppliedConfigurations applied, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => "configuration-drift";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => options.ConfigurationDriftWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects() =>
        registry.GetScopeNames().Where(scope =>
            liveness.GetScopeLiveness(scope).IsLive && !applied.StatusOf(scope, registry.GetScopeLabels(scope)).InSync);
}
