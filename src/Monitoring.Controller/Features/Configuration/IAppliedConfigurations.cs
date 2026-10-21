namespace Monitoring.Controller.Features.Configuration;

/// <summary>Which configuration the agent of each scope says it uses, to show when a change has not reached an agent.</summary>
public interface IAppliedConfigurations
{
    /// <summary>Records what an agent says it uses.</summary>
    /// <param name="scope">The scope the agent reports as.</param>
    /// <param name="connection">The connection it said it on; a newer connection of the same scope replaces an older one.</param>
    /// <param name="version">The version of the configuration it uses.</param>
    void Report(string scope, Guid connection, string version);

    /// <summary>Forgets what an agent said when its connection ends. Does nothing when a newer connection has replaced it.</summary>
    /// <param name="scope">The scope the agent reported as.</param>
    /// <param name="connection">The connection that ended.</param>
    void Disconnected(string scope, Guid connection);

    /// <summary>Compares what the agent of a scope uses with what it should use.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="labels">The labels of the scope, which decide which overrides apply.</param>
    /// <returns>The status.</returns>
    ConfigurationStatus StatusOf(string scope, IReadOnlyDictionary<string, string> labels);
}
