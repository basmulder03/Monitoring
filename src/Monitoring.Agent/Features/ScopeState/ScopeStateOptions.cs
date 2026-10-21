namespace Monitoring.Agent.Features.ScopeState;

/// <summary>Settings of the scope state slice.</summary>
public sealed class ScopeStateOptions
{
    /// <summary>
    /// Name of the scope this agent reports as, for example the namespace or the server name.
    /// It must match the scope the agent's token is allowed to report as.
    /// </summary>
    public string ScopeName { get; set; } = "";

    /// <summary>
    /// How many unsent deltas one controller connection may fall behind before it is cut off.
    /// A cut-off connection reconnects and starts again from a fresh snapshot.
    /// </summary>
    public int SubscriberBuffer { get; set; } = 1000;
}
