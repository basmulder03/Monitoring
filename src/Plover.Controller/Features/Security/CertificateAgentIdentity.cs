using System.Security.Cryptography.X509Certificates;
using Plover.Protocol.Messages;

namespace Plover.Controller.Features.Security;

/// <summary>Decides who an agent is from the client certificate it presented on a mutual TLS connection, which the transport has already checked against the authority of the controller.</summary>
public interface ICertificateIdentity
{
    /// <summary>Works out the scope an agent may report as from its certificate.</summary>
    /// <param name="hello">The first message of the connection; its token is not needed.</param>
    /// <param name="certificate">The certificate of the agent.</param>
    /// <returns>The agent's identity, or <see langword="null"/> when the certificate says nothing the controller accepts.</returns>
    AgentIdentity? Identify(Hello hello, X509Certificate2 certificate);
}

/// <summary>Takes the scope from the common name of the certificate, or from the name that <see cref="CertificateOptions.Scopes"/> gives it.</summary>
/// <param name="options">The names that are mapped to other scopes.</param>
internal sealed class CertificateAgentIdentity(CertificateOptions options) : ICertificateIdentity
{
    /// <inheritdoc />
    public AgentIdentity? Identify(Hello hello, X509Certificate2 certificate)
    {
        var name = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var scope = options.Scopes.TryGetValue(name, out var mapped) ? mapped : name;
        if (scope.Length > TokenAgentAuthenticator.MaxScopeNameLength)
        {
            return null;
        }

        // A certificate is for one scope: an agent that announces another one is not let in under this identity.
        var announced = hello.ScopeName?.Trim();
        return string.IsNullOrEmpty(announced) || string.Equals(announced, scope, StringComparison.Ordinal) ? new AgentIdentity(scope) : null;
    }
}

/// <summary>Settings of authentication by client certificate.</summary>
public sealed class CertificateOptions
{
    /// <summary>Whether a certificate that the transport accepted is enough to be let in. Needs the transport mode <c>mtls</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Common names of certificates that report as another scope than their name.</summary>
    public Dictionary<string, string> Scopes { get; set; } = new(StringComparer.Ordinal);
}
