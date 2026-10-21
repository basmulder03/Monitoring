using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Tests.Shared;

/// <summary>
/// Registry stand-in for slices that only read from the registry. Built up with <see cref="WithInstance"/>;
/// the write operations are not supported because readers must never call them.
/// </summary>
internal sealed class FakeScopeRegistry : IScopeRegistry
{
    private readonly SortedDictionary<string, List<InstanceInfo>> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _labels = new(StringComparer.Ordinal);
    private bool _failNextPurge;

    /// <summary>How many times <see cref="PurgeExpired"/> was called.</summary>
    public int PurgeCalls { get; private set; }

    /// <summary>Makes the next <see cref="PurgeExpired"/> call throw, once.</summary>
    /// <returns>This fake, for chaining.</returns>
    public FakeScopeRegistry WithFailingNextPurge()
    {
        _failNextPurge = true;
        return this;
    }

    /// <summary>Adds an instance to a scope, creating the scope when needed.</summary>
    /// <param name="scope">Scope that holds the instance.</param>
    /// <param name="service">Service the instance belongs to.</param>
    /// <param name="lifecycle">Lifecycle of the instance.</param>
    /// <param name="health">Health of the instance.</param>
    /// <param name="id">Instance id; generated from the service name and position when omitted.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeScopeRegistry WithInstance(
        string scope, string service, InstanceLifecycle lifecycle, HealthState health, string? id = null)
    {
        if (!_scopes.TryGetValue(scope, out var list))
        {
            _scopes[scope] = list = [];
        }

        list.Add(new InstanceInfo(id ?? $"{service}-{list.Count}", service, lifecycle, health, new Dictionary<string, string>()));
        return this;
    }

    /// <summary>Adds an instance exactly as given, creating the scope when needed.</summary>
    /// <param name="scope">Scope that holds the instance.</param>
    /// <param name="instance">The instance.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeScopeRegistry WithInstance(string scope, InstanceInfo instance)
    {
        if (!_scopes.TryGetValue(scope, out var list))
        {
            _scopes[scope] = list = [];
        }

        list.Add(instance);
        return this;
    }

    /// <summary>Sets a label of a scope, creating the scope when needed.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="key">Label name.</param>
    /// <param name="value">Label value.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeScopeRegistry WithLabel(string scope, string key, string value)
    {
        if (!_scopes.ContainsKey(scope))
        {
            _scopes[scope] = [];
        }

        if (!_labels.TryGetValue(scope, out var labels))
        {
            _labels[scope] = labels = [];
        }

        labels[key] = value;
        return this;
    }

    /// <summary>Removes every instance of a scope but keeps the scope known.</summary>
    /// <param name="scope">The scope to empty.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeScopeRegistry WithEmptyScope(string scope)
    {
        _scopes[scope] = [];
        return this;
    }

    public IReadOnlyList<InstanceInfo> GetInstances(string scopeName) =>
        _scopes.TryGetValue(scopeName, out var list) ? list : [];

    public IReadOnlyDictionary<string, string> GetScopeLabels(string scopeName) =>
        _labels.TryGetValue(scopeName, out var labels) ? labels : new Dictionary<string, string>();

    public IReadOnlyList<string> GetScopeNames() => [.. _scopes.Keys];

    public ApplyResult ApplySnapshot(Snapshot snapshot) => throw new NotSupportedException();

    public ApplyResult ApplyDelta(string scopeName, Delta delta) => throw new NotSupportedException();

    public void PurgeExpired()
    {
        PurgeCalls++;
        if (_failNextPurge)
        {
            _failNextPurge = false;
            throw new InvalidOperationException("purge failed");
        }
    }
}
