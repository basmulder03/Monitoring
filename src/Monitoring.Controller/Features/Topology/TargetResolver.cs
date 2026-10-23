using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Topology;

/// <summary>Works out which node a call went to, from the address an application used.</summary>
/// <remarks>
/// Resolution uses what the registry knows: pod addresses, and the names of services within and across scopes. A name that matches several
/// services in other scopes is not guessed at; it stays an external node, because a wrong edge is worse than a missing one.
/// </remarks>
internal sealed class TargetResolver
{
    private readonly Dictionary<string, (string Scope, string Service)> _byAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string Scope, string Service)> _services = [];

    /// <summary>The names Kubernetes Services give to the services of a scope, which are often not the name of the application.</summary>
    private readonly Dictionary<(string Scope, string Name), string> _aliases = [];

    /// <summary>Creates the resolver from the live instances of every scope.</summary>
    /// <param name="instances">Each live instance with the scope it belongs to.</param>
    public TargetResolver(IEnumerable<(string Scope, InstanceInfo Instance)> instances)
    {
        foreach (var (scope, instance) in instances)
        {
            _services.Add((scope, instance.ServiceName));
            foreach (var name in Split(instance.Labels.GetValueOrDefault("k8s.services")))
            {
                _aliases.TryAdd((scope, name), instance.ServiceName);
            }

            foreach (var address in Split(instance.Labels.GetValueOrDefault("k8s.service-ips")))
            {
                _byAddress[address] = (scope, instance.ServiceName);
            }

            if (instance.Labels.TryGetValue("k8s.pod-ip", out var ip) && !string.IsNullOrWhiteSpace(ip))
            {
                _byAddress[ip] = (scope, instance.ServiceName);
            }
        }
    }

    /// <summary>Gets the id of the node a call went to, and what to show for it.</summary>
    /// <param name="callerScope">The scope of the caller, whose services are preferred for a plain name.</param>
    /// <param name="type">How it was called.</param>
    /// <param name="target">The address as the caller used it.</param>
    /// <returns>The node.</returns>
    public TopologyNode Resolve(string callerScope, string type, string target)
    {
        switch (type)
        {
            case "DB":
                return new TopologyNode($"database:{target}", target, NodeKind.Database, null, null);

            case "MQ":
                return new TopologyNode($"queue:{target}", target, NodeKind.Queue, null, null);
        }

        var host = HostOf(target);
        if (_byAddress.TryGetValue(host, out var byIp))
        {
            return Service(byIp.Scope, byIp.Service);
        }

        var labels = host.Split('.');
        var name = labels[0];

        // service.namespace[.svc[.cluster.local]]: the second label names the scope.
        if (labels.Length >= 2 && Find(labels[1], name) is { } named)
        {
            return Service(labels[1], named);
        }

        // A plain name is a service of the caller's own scope, as cluster DNS resolves it.
        if (labels.Length == 1 && Find(callerScope, name) is { } own)
        {
            return Service(callerScope, own);
        }

        return new TopologyNode($"external:{target}", target, NodeKind.External, null, null);
    }

    /// <summary>The service a name stands for in a scope: a service of that name, else the one a Kubernetes Service of that name selects.</summary>
    private string? Find(string scope, string name) =>
        _services.Contains((scope, name)) ? name : _aliases.GetValueOrDefault((scope, name));

    private static string[] Split(string? list) => list?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static TopologyNode Service(string scope, string service) =>
        new($"service:{scope}/{service}", service, NodeKind.Service, scope, null);

    private static string HostOf(string target)
    {
        var colon = target.LastIndexOf(':');
        return colon > 0 && target.IndexOf(':') == colon ? target[..colon] : target;
    }
}
