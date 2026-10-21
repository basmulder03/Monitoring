namespace Monitoring.Controller.Features.Security;

/// <summary>One accepted agent token, stored only as a hash.</summary>
/// <param name="ScopeName">The scope an agent presenting this token may report as.</param>
/// <param name="TokenHash">SHA-256 of the token as hexadecimal text, as produced by <see cref="AgentTokenHash.Compute"/>.</param>
public sealed record AgentTokenEntry(string ScopeName, string TokenHash);
