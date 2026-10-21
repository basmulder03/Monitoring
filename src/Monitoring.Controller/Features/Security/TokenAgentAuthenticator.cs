using System.Security.Cryptography;
using System.Text;
using Monitoring.Protocol.Messages;

namespace Monitoring.Controller.Features.Security;

/// <summary>
/// Authenticates agents by the token in their <see cref="Hello"/>, compared by hash. A token bound to a scope only lets
/// the agent report as that scope; an enrollment token lets the agent register under the scope name it announces.
/// </summary>
internal sealed class TokenAgentAuthenticator : IAgentAuthenticator
{
    /// <summary>Longest scope name an agent may announce; it ends up in logs and on the dashboard.</summary>
    internal const int MaxScopeNameLength = 253;

    private readonly IReadOnlyList<(AgentTokenEntry Entry, byte[] Hash)> _tokens;

    /// <summary>Creates the authenticator from the configured token hashes.</summary>
    /// <param name="options">The accepted tokens.</param>
    /// <exception cref="FormatException">A configured hash is not valid hexadecimal text.</exception>
    public TokenAgentAuthenticator(SecurityOptions options)
    {
        _tokens = [.. options.AgentTokens.Select(entry => (entry, Convert.FromHexString(entry.TokenHash)))];
    }

    /// <inheritdoc />
    public ValueTask<AgentIdentity?> AuthenticateAsync(Hello hello, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(hello.Token))
        {
            return ValueTask.FromResult<AgentIdentity?>(null);
        }

        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(hello.Token));
        var announced = hello.ScopeName?.Trim();

        // Compare against every entry without stopping at the first match, so the time taken does not
        // reveal which entry matched.
        AgentIdentity? identity = null;
        foreach (var (entry, hash) in _tokens)
        {
            if (CryptographicOperations.FixedTimeEquals(presented, hash))
            {
                identity ??= IdentityFor(entry, announced);
            }
        }

        return ValueTask.FromResult(identity);
    }

    /// <summary>The identity a matching token grants, or null when the scope the agent announces is not allowed.</summary>
    private static AgentIdentity? IdentityFor(AgentTokenEntry entry, string? announced)
    {
        if (entry.AllowsAnyScope)
        {
            return IsValidScopeName(announced) ? new AgentIdentity(announced!) : null;
        }

        // A bound token also covers agents that announce nothing, but never lets an agent report as another scope.
        return string.IsNullOrEmpty(announced) || announced == entry.ScopeName ? new AgentIdentity(entry.ScopeName) : null;
    }

    private static bool IsValidScopeName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name != AgentTokenEntry.AnyScope
        && name.Length <= MaxScopeNameLength
        && !name.Any(char.IsControl);
}
