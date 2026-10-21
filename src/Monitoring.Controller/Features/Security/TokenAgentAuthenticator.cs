using System.Security.Cryptography;
using System.Text;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Security;

/// <summary>
/// Authenticates agents by the per-scope token in their <see cref="Hello"/>, compared by hash.
/// </summary>
internal sealed class TokenAgentAuthenticator : IAgentAuthenticator
{
    private readonly IReadOnlyList<(string ScopeName, byte[] Hash)> _tokens;

    /// <summary>Creates the authenticator from the configured token hashes.</summary>
    /// <param name="options">The accepted tokens.</param>
    /// <exception cref="FormatException">A configured hash is not valid hexadecimal text.</exception>
    public TokenAgentAuthenticator(SecurityOptions options)
    {
        _tokens = [.. options.AgentTokens.Select(entry => (entry.ScopeName, Convert.FromHexString(entry.TokenHash)))];
    }

    /// <inheritdoc />
    public ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(hello.Token))
        {
            return ValueTask.FromResult<AgentIdentity?>(null);
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(hello.Token));

        // Compare against every entry without stopping at the first match, so the time taken does not
        // reveal which entry matched.
        string? matchedScope = null;
        foreach (var (scopeName, hash) in _tokens)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, hash))
            {
                matchedScope = scopeName;
            }
        }

        return ValueTask.FromResult<AgentIdentity?>(matchedScope is null ? null : new AgentIdentity(matchedScope));
    }
}
