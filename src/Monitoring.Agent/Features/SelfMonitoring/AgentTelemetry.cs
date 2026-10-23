namespace Monitoring.Agent.Features.SelfMonitoring;

/// <summary>Default <see cref="IAgentTelemetry"/>: a value per name, under one lock.</summary>
internal sealed class AgentTelemetry : IAgentTelemetry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, double> _values = [];

    /// <inheritdoc />
    public void Add(string name, double delta = 1)
    {
        lock (_gate)
        {
            _values[name] = _values.GetValueOrDefault(name) + delta;
        }
    }

    /// <inheritdoc />
    public void Set(string name, double value)
    {
        lock (_gate)
        {
            _values[name] = value;
        }
    }

    /// <summary>Gets the values as they are now.</summary>
    /// <returns>A copy, by name.</returns>
    public IReadOnlyDictionary<string, double> Read()
    {
        lock (_gate)
        {
            return new Dictionary<string, double>(_values);
        }
    }
}
