namespace Monitoring.Controller.Features.StateStore;

/// <summary>Keeps the logs in memory: fast and free, and gone when the controller stops.</summary>
internal sealed class MemoryStateStore : IStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<string>> _logs = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<string> Read(string log)
    {
        lock (_gate)
        {
            return _logs.TryGetValue(log, out var values) ? [.. values] : [];
        }
    }

    /// <inheritdoc />
    public void Append(string log, string json, int keep)
    {
        lock (_gate)
        {
            if (!_logs.TryGetValue(log, out var values))
            {
                _logs[log] = values = [];
            }

            values.Add(json);
            if (values.Count > keep)
            {
                values.RemoveRange(0, values.Count - keep);
            }
        }
    }
}
