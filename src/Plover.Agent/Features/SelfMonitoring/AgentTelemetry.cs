namespace Plover.Agent.Features.SelfMonitoring;

/// <summary>Default <see cref="IAgentTelemetry"/>: a value per name, under one lock.</summary>
internal sealed class AgentTelemetry : IAgentTelemetry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Name, string? Replica), double> _values = [];

    /// <inheritdoc />
    public void Add(string name, double delta = 1)
    {
        lock (_gate)
        {
            _values[(name, null)] = _values.GetValueOrDefault((name, null)) + delta;
        }
    }

    /// <inheritdoc />
    public void Set(string name, double value, string? replica = null)
    {
        lock (_gate)
        {
            _values[(name, replica)] = value;
        }
    }

    /// <inheritdoc />
    public void Forget(string replica)
    {
        lock (_gate)
        {
            foreach (var key in _values.Keys.Where(key => key.Replica == replica).ToList())
            {
                _values.Remove(key);
            }
        }
    }

    /// <summary>Gets the values as they are now.</summary>
    /// <returns>A copy, by name and replica.</returns>
    public IReadOnlyDictionary<(string Name, string? Replica), double> Read()
    {
        lock (_gate)
        {
            return new Dictionary<(string Name, string? Replica), double>(_values);
        }
    }
}
