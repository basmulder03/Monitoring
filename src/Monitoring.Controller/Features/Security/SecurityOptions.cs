namespace Monitoring.Controller.Features.Security;

/// <summary>Settings of the security slice.</summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// The accepted agent tokens. Use <see cref="AgentTokenEntry.AnyScope"/> for an enrollment token that lets agents register
    /// themselves. A scope may have several entries, so a new token can be introduced before the old one is removed during rotation.
    /// </summary>
    public List<AgentTokenEntry> AgentTokens { get; set; } = [];

    /// <summary>Gets or sets whether and how agents may use the token of their Kubernetes service account instead.</summary>
    public ServiceAccountOptions ServiceAccounts { get; set; } = new();

    /// <summary>Gets or sets whether agents may be let in by the client certificate of a mutual TLS connection, without a token.</summary>
    public CertificateOptions Certificates { get; set; } = new();
}
