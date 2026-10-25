namespace Plover.Controller.Features.Configuration;

/// <summary>The central configuration, as it applies to each agent.</summary>
public interface IEffectiveConfiguration
{
    /// <summary>Works out what an agent is to use: the built-in defaults, then the controller's defaults, then every override that matches the agent.</summary>
    /// <param name="scope">The scope the agent reports as.</param>
    /// <param name="labels">The labels of that scope.</param>
    /// <returns>The settings, with a version that changes whenever any of them does.</returns>
    AgentSettings For(string scope, IReadOnlyDictionary<string, string> labels);

    /// <summary>Asks to be told whenever the configuration was replaced by a different one.</summary>
    /// <param name="onChanged">Called on the thread that replaced it; it must not block.</param>
    /// <returns>Disposing it stops the calls.</returns>
    IDisposable OnChanged(Action onChanged);
}
