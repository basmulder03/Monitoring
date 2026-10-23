using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>What an application reports about one queue, taken over its instances: the highest number each of them reports, because they ask the same broker.</summary>
/// <param name="Depth">Messages waiting, or <see langword="null"/> when not reported.</param>
/// <param name="Consumers">Consumers attached, or <see langword="null"/> when not reported.</param>
/// <param name="DeadLetters">Dead letters, or <see langword="null"/> when not reported.</param>
internal sealed record QueueReading(double? Depth, double? Consumers, double? DeadLetters);

/// <summary>Fires for a queue that an application watches (<c>monitoring.queue.*</c>) when <paramref name="isFailing"/> says so.</summary>
/// <param name="name">The name of the rule.</param>
/// <param name="severity">Severity of the alerts.</param>
/// <param name="wait">How long the queue must stay failing before the alert fires.</param>
/// <param name="registry">Source of the instances.</param>
/// <param name="isFailing">Decides from the numbers of one queue.</param>
internal sealed class QueueRule(string name, AlertSeverity severity, TimeSpan wait, IScopeRegistry registry, Func<QueueReading, bool> isFailing) : IAlertRule
{
    /// <summary>Messages waiting.</summary>
    public const string DepthMetric = "monitoring.queue.depth";

    /// <summary>Consumers attached.</summary>
    public const string ConsumersMetric = "monitoring.queue.consumers";

    /// <summary>Messages in the dead-letter queue.</summary>
    public const string DeadLettersMetric = "monitoring.queue.dead_letters";

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public AlertSeverity Severity => severity;

    /// <inheritdoc />
    public TimeSpan Wait => wait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var readings = new Dictionary<string, QueueReading>(StringComparer.Ordinal);
        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone && i.Metrics is not null))
            {
                foreach (var series in instance.Metrics!.Where(m => m.Name is DepthMetric or ConsumersMetric or DeadLettersMetric))
                {
                    var subject = $"{scope}/{instance.ServiceName}/{series.Labels.GetValueOrDefault("queue", "queue")}";
                    var known = readings.GetValueOrDefault(subject) ?? new QueueReading(null, null, null);
                    readings[subject] = series.Name switch
                    {
                        DepthMetric => known with { Depth = Max(known.Depth, series.Value) },
                        ConsumersMetric => known with { Consumers = Max(known.Consumers, series.Value) },
                        _ => known with { DeadLetters = Max(known.DeadLetters, series.Value) },
                    };
                }
            }
        }

        return readings.Where(pair => isFailing(pair.Value)).Select(pair => pair.Key);
    }

    private static double Max(double? known, double value) => known is { } k ? Math.Max(k, value) : value;
}
