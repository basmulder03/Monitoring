namespace Monitoring.Controller.Features.Liveness;

/// <summary>Default <see cref="IAgentLiveness"/>: the time of the last message per connection, judged against the clock when asked.</summary>
/// <param name="options">Provides the heartbeat timeout.</param>
/// <param name="time">Clock for the timeout.</param>
internal sealed class AgentLiveness(LivenessOptions options, TimeProvider time) : IAgentLiveness
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ScopeConnections> _scopes = [];

    /// <inheritdoc />
    public void Connected(string scopeName, Guid connectionId)
    {
        lock (_gate)
        {
            if (!_scopes.TryGetValue(scopeName, out var scope))
            {
                _scopes[scopeName] = scope = new ScopeConnections();
            }

            var now = time.GetUtcNow();
            scope.Connections[connectionId] = now;
            scope.LastSeen = now;
        }
    }

    /// <inheritdoc />
    public void Seen(string scopeName, Guid connectionId)
    {
        lock (_gate)
        {
            // A message on a connection that was never registered or already ended is ignored.
            if (_scopes.TryGetValue(scopeName, out var scope) && scope.Connections.ContainsKey(connectionId))
            {
                var now = time.GetUtcNow();
                scope.Connections[connectionId] = now;
                scope.LastSeen = now;
            }
        }
    }

    /// <inheritdoc />
    public void Disconnected(string scopeName, Guid connectionId)
    {
        lock (_gate)
        {
            if (_scopes.TryGetValue(scopeName, out var scope) && scope.Connections.Remove(connectionId))
            {
                // The agent is gone as of now, whatever was heard before.
                scope.LastSeen = time.GetUtcNow();
            }
        }
    }

    /// <inheritdoc />
    public ScopeLiveness GetScopeLiveness(string scopeName)
    {
        lock (_gate)
        {
            if (!_scopes.TryGetValue(scopeName, out var scope))
            {
                return new ScopeLiveness(false, null);
            }

            var now = time.GetUtcNow();
            var live = scope.Connections.Values.Any(lastSeen => now - lastSeen < options.Timeout);
            return new ScopeLiveness(live, scope.LastSeen);
        }
    }

    /// <summary>The connections of one scope and when it was last heard from.</summary>
    private sealed class ScopeConnections
    {
        public Dictionary<Guid, DateTimeOffset> Connections { get; } = [];

        public DateTimeOffset? LastSeen { get; set; }
    }
}
