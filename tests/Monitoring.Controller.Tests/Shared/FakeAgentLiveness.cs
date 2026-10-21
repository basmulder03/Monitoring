using Monitoring.Controller.Features.Liveness;

namespace Monitoring.Controller.Tests.Shared;

/// <summary>
/// Liveness stand-in for slices that only ask whether a scope is alive, or only report to it. Scopes count as live unless
/// <see cref="WithDeadScope"/> says otherwise, and every report is recorded.
/// </summary>
internal sealed class FakeAgentLiveness : IAgentLiveness
{
    private readonly Dictionary<string, ScopeLiveness> _dead = new(StringComparer.Ordinal);
    private readonly List<string> _events = [];

    /// <summary>What was reported, in order, as <c>connected:scope</c>, <c>seen:scope</c> or <c>disconnected:scope</c>.</summary>
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>Makes a scope count as not live.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="lastSeen">When its agent was last heard from.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeAgentLiveness WithDeadScope(string scope, DateTimeOffset? lastSeen = null)
    {
        _dead[scope] = new ScopeLiveness(false, lastSeen);
        return this;
    }

    public void Connected(string scopeName, Guid connectionId) => Record($"connected:{scopeName}");

    public void Seen(string scopeName, Guid connectionId) => Record($"seen:{scopeName}");

    public void Disconnected(string scopeName, Guid connectionId) => Record($"disconnected:{scopeName}");

    public ScopeLiveness GetScopeLiveness(string scopeName) =>
        _dead.TryGetValue(scopeName, out var dead) ? dead : new ScopeLiveness(true, null);

    private void Record(string entry)
    {
        lock (_events)
        {
            _events.Add(entry);
        }
    }
}
