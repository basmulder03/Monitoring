namespace Monitoring.Controller.Features.Security;

/// <summary>Settings of the security slice.</summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// The accepted agent tokens. A scope may have several entries, so a new token can be introduced
    /// before the old one is removed during rotation.
    /// </summary>
    public List<AgentTokenEntry> AgentTokens { get; set; } = [];
}
