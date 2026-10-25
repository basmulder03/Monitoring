using Plover.Controller.Features.Registry;
using Plover.Protocol.Instances;

namespace Plover.Controller.Features.Alerting;

/// <summary>Fires for a database connection pool of a service that has no free connection left, as the provider reports in <c>db.client.connections.usage</c> and <c>db.client.connections.max</c> (Npgsql does).</summary>
/// <remarks>A pool that is full for a moment is normal under load, so the rule waits; one that stays full makes requests queue for a connection.</remarks>
/// <param name="wait">How long a pool must stay full before the alert fires.</param>
/// <param name="registry">Source of the instances.</param>
internal sealed class ConnectionPoolRule(TimeSpan wait, IScopeRegistry registry) : IAlertRule
{
    private const string UsageMetric = "db.client.connections.usage";
    private const string MaxMetric = "db.client.connections.max";

    /// <inheritdoc />
    public string Name => "connection-pool-full";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => wait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var full = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone && i.Metrics is not null))
            {
                var metrics = instance.Metrics!;
                foreach (var max in metrics.Where(m => m.Name == MaxMetric && m.Value > 0))
                {
                    var pool = max.Labels.GetValueOrDefault("pool.name", "pool");
                    var used = metrics.Where(m => m.Name == UsageMetric && m.Labels.GetValueOrDefault("state") == "used" && m.Labels.GetValueOrDefault("pool.name", "pool") == pool);
                    if (used.Any(m => m.Value >= max.Value))
                    {
                        full.Add($"{scope}/{instance.ServiceName}/{pool}");
                    }
                }
            }
        }

        return full;
    }
}
