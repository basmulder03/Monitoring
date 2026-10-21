using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;

namespace Monitoring.Controller.Features.Packages;

/// <summary>Builds the inventory from the instances in the registry each time it is asked, so it is always as current as the registry.</summary>
/// <param name="registry">Source of the instances and the packages they reported.</param>
internal sealed class PackageInventory(IScopeRegistry registry) : IPackageInventory
{
    /// <inheritdoc />
    public PackageCatalog GetCatalog()
    {
        var all = LiveInstances().ToList();
        var services = all.GroupBy(i => (i.Scope, i.Instance.ServiceName)).ToList();
        var reported = all.Where(i => i.Instance.Packages is { Count: > 0 }).ToList();

        var entries = reported
            .SelectMany(entry => entry.Instance.Packages!.Select(package => (entry.Scope, entry.Instance.ServiceName, Package: package)))
            .GroupBy(item => item.Package.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(ToEntry)
            .ToList();

        var withList = reported.Select(i => (i.Scope, i.Instance.ServiceName)).Distinct().Count();
        return new PackageCatalog(entries, withList, services.Count - withList);
    }

    /// <inheritdoc />
    public PackageEntry? Find(string name) =>
        GetCatalog().Packages.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public ServicePackages? ForService(string scope, string service) =>
        GetAll().FirstOrDefault(entry => entry.Scope == scope && entry.Service == service);

    /// <inheritdoc />
    public IReadOnlyList<ServicePackages> GetAll() =>
    [
        .. LiveInstances()
            .Where(i => i.Instance.Packages is { Count: > 0 })
            .GroupBy(i => (i.Scope, i.Instance.ServiceName))
            .OrderBy(group => group.Key.Scope, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ServiceName, StringComparer.Ordinal)
            .Select(group =>
            {
                // Instances of one service can run different versions during a rollout; the newest says what the service is now.
                var newest = group.OrderByDescending(i => i.Instance.Version ?? "", VersionOrder.Instance).First().Instance;
                return new ServicePackages(
                    group.Key.Scope, group.Key.ServiceName, newest.Version, [.. newest.Packages!.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)]);
            }),
    ];

    private IEnumerable<(string Scope, InstanceInfo Instance)> LiveInstances() =>
        registry.GetScopeNames().SelectMany(scope => registry.GetInstances(scope)
            .Where(instance => instance.Lifecycle is InstanceLifecycle.Starting or InstanceLifecycle.Running or InstanceLifecycle.Terminating)
            .Select(instance => (scope, instance)));

    private static PackageEntry ToEntry(IGrouping<string, (string Scope, string ServiceName, PackageReport Package)> group)
    {
        var versions = group
            .GroupBy(item => item.Package.Version, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(byVersion => byVersion.Key, VersionOrder.Instance)
            .Select(byVersion => new PackageVersion(
                byVersion.Key,
                [.. byVersion
                    .GroupBy(item => (item.Scope, item.ServiceName))
                    .OrderBy(service => service.Key.Scope, StringComparer.Ordinal)
                    .ThenBy(service => service.Key.ServiceName, StringComparer.Ordinal)
                    .Select(service => new PackageUsage(service.Key.Scope, service.Key.ServiceName, service.Count(), service.Any(item => item.Package.Direct)))]))
            .ToList();
        return new PackageEntry(group.First().Package.Name, versions);
    }
}
