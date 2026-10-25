using Plover.Controller.Features.Registry;
using Plover.Protocol.Instances;

namespace Plover.Controller.Features.Alerting;

/// <summary>Fires for a service whose calls to a dependency fail too often within a window, which is what a failing database or a broken downstream service looks like from the caller.</summary>
/// <remarks>The calls of all instances of the service count together, and a dependency that is hardly called does not raise it, because a few failures of few calls say nothing.</remarks>
/// <param name="registry">Source of the instances and their calls.</param>
/// <param name="options">Provides the window, the share of failures and the fewest calls that count.</param>
/// <param name="time">Clock for the window.</param>
internal sealed class EdgeErrorRateRule(IScopeRegistry registry, AlertingOptions options, TimeProvider time) : IAlertRule
{
    private readonly RollingCounters _calls = new(options.EdgeErrorWindow, time);
    private readonly RollingCounters _errors = new(options.EdgeErrorWindow, time);

    /// <inheritdoc />
    public string Name => "edge-error-rate";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => TimeSpan.Zero;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var calls = new Dictionary<string, double>(StringComparer.Ordinal);
        var errors = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone && i.Edges is not null))
            {
                foreach (var edge in instance.Edges!)
                {
                    var counter = $"{scope}/{instance.Id}/{edge.Type}/{edge.Target}";
                    var subject = $"{scope}/{instance.ServiceName} -> {edge.Target}";
                    calls[subject] = calls.GetValueOrDefault(subject) + _calls.Observe(counter, edge.Calls);
                    errors[subject] = errors.GetValueOrDefault(subject) + _errors.Observe(counter, edge.Errors);
                }
            }
        }

        _calls.ForgetUnseen();
        _errors.ForgetUnseen();
        return calls.Where(pair => pair.Value >= options.EdgeErrorMinCalls && errors[pair.Key] / pair.Value >= options.EdgeErrorShare).Select(pair => pair.Key);
    }
}
