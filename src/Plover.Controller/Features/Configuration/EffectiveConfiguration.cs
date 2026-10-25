using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Configuration;

/// <summary>Holds the current central configuration and tells those who asked when it was replaced.</summary>
/// <param name="logger">Receives what was applied and what was rejected.</param>
internal sealed class EffectiveConfiguration(ILogger<EffectiveConfiguration> logger) : IEffectiveConfiguration
{
    private readonly Lock _gate = new();
    private readonly List<Action> _listeners = [];
    private volatile ParsedConfiguration _current = ParsedConfiguration.BuiltIn;

    /// <inheritdoc />
    public AgentSettings For(string scope, IReadOnlyDictionary<string, string> labels) => _current.For(scope, labels);

    /// <inheritdoc />
    public IDisposable OnChanged(Action onChanged)
    {
        lock (_gate)
        {
            _listeners.Add(onChanged);
        }

        return new Subscription(this, onChanged);
    }

    /// <summary>Uses a new configuration from now on, unless it is the same as the current one.</summary>
    /// <param name="replacement">The configuration to use.</param>
    public void Replace(ParsedConfiguration replacement)
    {
        Action[] listeners;
        lock (_gate)
        {
            if (_current.SameAs(replacement))
            {
                return;
            }

            _current = replacement;
            listeners = [.. _listeners];
        }

        logger.LogInformation("The central configuration was replaced");
        foreach (var listener in listeners)
        {
            try
            {
                listener();
            }
            catch (Exception ex)
            {
                // One listener failing must not keep the others from hearing about it.
                logger.LogError(ex, "Telling a connection about the new configuration failed");
            }
        }
    }

    private void Remove(Action listener)
    {
        lock (_gate)
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class Subscription(EffectiveConfiguration owner, Action listener) : IDisposable
    {
        public void Dispose() => owner.Remove(listener);
    }
}
