using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.ChangeMarkers;

/// <summary>
/// Default <see cref="IChangeMarkers"/>: compares the versions in use per service with those of the previous sample.
/// The first sample of a service is its baseline and raises nothing, so a restarted replica does not report every service as changed.
/// </summary>
/// <param name="registry">Provides the instances.</param>
/// <param name="options">Provides the number of markers to keep.</param>
/// <param name="time">Clock for the markers.</param>
internal sealed class ChangeTracker(IScopeRegistry registry, ChangeMarkersOptions options, TimeProvider time) : IChangeMarkers
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Scope, string Service), SortedSet<string>> _versions = [];
    private readonly List<ChangeMarker> _markers = [];

    /// <summary>Reads the registry and records a marker for each version that is new to a service.</summary>
    public void Sample()
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            foreach (var scope in registry.GetScopeNames())
            {
                var current = registry.GetInstances(scope)
                    .Where(instance => instance.Lifecycle != InstanceLifecycle.Gone && instance.Version is not null)
                    .GroupBy(instance => instance.ServiceName)
                    .ToDictionary(group => group.Key, group => new SortedSet<string>(group.Select(instance => instance.Version!), StringComparer.Ordinal));

                foreach (var (service, versions) in current)
                {
                    if (_versions.TryGetValue((scope, service), out var before))
                    {
                        // Only versions that were not there at the last sample count: instances going away is not a change.
                        foreach (var added in versions.Except(before, StringComparer.Ordinal))
                        {
                            _markers.Add(new ChangeMarker(now, scope, service, string.Join(", ", before), added));
                        }
                    }

                    // A service with no live version left keeps its last one, so coming back with another still counts.
                    _versions[(scope, service)] = versions;
                }
            }

            if (_markers.Count > options.MaxMarkers)
            {
                _markers.RemoveRange(0, _markers.Count - options.MaxMarkers);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChangeMarker> GetRecent()
    {
        lock (_gate)
        {
            return [.. Enumerable.Reverse(_markers)];
        }
    }
}
