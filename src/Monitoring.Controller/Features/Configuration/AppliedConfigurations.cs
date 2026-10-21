namespace Monitoring.Controller.Features.Configuration;

/// <summary>Default <see cref="IAppliedConfigurations"/>: remembers the latest version each connected agent reported. Safe to use from any thread.</summary>
/// <param name="configuration">Says what each agent should use.</param>
internal sealed class AppliedConfigurations(IEffectiveConfiguration configuration) : IAppliedConfigurations
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (Guid Connection, string Version)> _applied = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Report(string scope, Guid connection, string version)
    {
        lock (_gate)
        {
            _applied[scope] = (connection, version);
        }
    }

    /// <inheritdoc />
    public void Disconnected(string scope, Guid connection)
    {
        lock (_gate)
        {
            if (_applied.TryGetValue(scope, out var known) && known.Connection == connection)
            {
                _applied.Remove(scope);
            }
        }
    }

    /// <inheritdoc />
    public ConfigurationStatus StatusOf(string scope, IReadOnlyDictionary<string, string> labels)
    {
        string? applied;
        lock (_gate)
        {
            applied = _applied.TryGetValue(scope, out var known) ? known.Version : null;
        }

        return new ConfigurationStatus(applied, configuration.For(scope, labels).Version);
    }
}
