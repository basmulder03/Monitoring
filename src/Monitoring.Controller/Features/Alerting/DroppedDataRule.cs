using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for an instance whose monitoring client keeps throwing spans away, because its buffer is too small for the traffic and what someone opens in the dashboard is then not what happened.</summary>
/// <param name="registry">Source of the instances and their metrics.</param>
/// <param name="options">Provides the window and how many dropped spans count.</param>
/// <param name="time">Clock for the window.</param>
internal sealed class DroppedDataRule(IScopeRegistry registry, AlertingOptions options, TimeProvider time) : IAlertRule
{
    /// <summary>The metric in which the client reports how many spans it threw away.</summary>
    public const string MetricName = "monitoring.client.spans_dropped";

    private readonly RollingCounters _dropped = new(options.DroppedDataWindow, time);

    /// <inheritdoc />
    public string Name => "dropped-data";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => TimeSpan.Zero;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var failing = new List<string>();
        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone && i.Metrics is not null))
            {
                var series = instance.Metrics!.FirstOrDefault(metric => metric.Name == MetricName);
                if (series is not null && _dropped.Observe($"{scope}/{instance.Id}", series.Value) >= options.DroppedDataSpans)
                {
                    failing.Add($"{scope}/{instance.ServiceName}/{instance.Id}");
                }
            }
        }

        _dropped.ForgetUnseen();
        return failing;
    }
}
