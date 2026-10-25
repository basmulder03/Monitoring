using Plover.Controller.Features.Registry;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;

namespace Plover.Controller.Features.Topology;

/// <summary>Builds the dependency graph from what the instances report about their calls, and lets idle edges fade.</summary>
/// <remarks>
/// Applications report running totals, so a call is only known to have happened when a total grows. The tracker remembers when each total last grew;
/// an edge nobody called for <see cref="TopologyOptions.EdgeFadeAfter"/> is left out. A restarted controller starts that clock again, so edges show for one fade period at most.
/// </remarks>
/// <param name="registry">Source of the instances and what they report.</param>
/// <param name="options">Provides how long an idle edge stays.</param>
/// <param name="time">Clock for the fade.</param>
internal sealed class TopologyTracker(IScopeRegistry registry, TopologyOptions options, TimeProvider time) : ITopology
{
    private readonly Dictionary<(string Instance, string Type, string Target), (long Calls, DateTimeOffset ChangedAt)> _seen = [];
    private volatile TopologyGraph _graph = TopologyGraph.Empty;

    /// <inheritdoc />
    public TopologyGraph GetGraph() => _graph;

    /// <summary>Reads the registry and updates the graph. Called by one thread at a time.</summary>
    public void Sample()
    {
        var now = time.GetUtcNow();
        var live = registry.GetScopeNames()
            .SelectMany(scope => registry.GetInstances(scope)
                .Where(i => i.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running)
                .Select(i => (Scope: scope, Instance: i)))
            .ToList();

        var resolver = new TargetResolver(live);
        var nodes = new Dictionary<string, TopologyNode>(StringComparer.Ordinal);
        var edges = new Dictionary<(string From, string To, string Type), (long Calls, long Errors, double Duration)>();
        var stillThere = new HashSet<(string, string, string)>();

        foreach (var (scope, instance) in live.Where(entry => entry.Instance.Edges is not null))
        {
            var from = new TopologyNode($"service:{scope}/{instance.ServiceName}", instance.ServiceName, NodeKind.Service, scope, null);
            foreach (var edge in instance.Edges!)
            {
                var key = ($"{scope}/{instance.Id}", edge.Type, edge.Target);
                stillThere.Add(key);
                if (!_seen.TryGetValue(key, out var seen) || seen.Calls != edge.Calls)
                {
                    _seen[key] = (edge.Calls, now);
                }
                else if (now - seen.ChangedAt >= options.EdgeFadeAfter)
                {
                    continue;
                }

                // A service that takes messages from a queue is reached by the queue, so the edge points from the queue to the service.
                var consumes = edge.Type == "MQ-IN";
                var type = consumes ? "MQ" : edge.Type;
                var to = resolver.Resolve(scope, type, edge.Target);
                nodes[from.Id] = from;
                nodes[to.Id] = to;
                var (source, destination) = consumes ? (to.Id, from.Id) : (from.Id, to.Id);
                var total = edges.GetValueOrDefault((source, destination, type));
                edges[(source, destination, type)] = (total.Calls + edge.Calls, total.Errors + edge.Errors, total.Duration + edge.DurationSumMs);
            }
        }

        // Forget instances that are gone, so the memory of what was seen never outgrows the instances.
        foreach (var key in _seen.Keys.Where(key => !stillThere.Contains(key)).ToList())
        {
            _seen.Remove(key);
        }

        _graph = new TopologyGraph(
            [.. nodes.Values.Select(node => WithHealth(node, live)).OrderBy(node => node.Id, StringComparer.Ordinal)],
            [.. edges
                .OrderBy(pair => pair.Key.From, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.To, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Type, StringComparer.Ordinal)
                .Select(pair => new TopologyEdge(pair.Key.From, pair.Key.To, pair.Key.Type, pair.Value.Calls, pair.Value.Errors, pair.Value.Duration))]);
    }

    /// <summary>Adds the health of a service: the worst of its live instances.</summary>
    private static TopologyNode WithHealth(TopologyNode node, List<(string Scope, InstanceInfo Instance)> live)
    {
        if (node.Kind != NodeKind.Service)
        {
            return node;
        }

        var health = live
            .Where(entry => entry.Scope == node.Scope && entry.Instance.ServiceName == node.Name)
            .Select(entry => entry.Instance.Health)
            .Worst();
        return node with { Health = health };
    }
}
