namespace Monitoring.Controller.Features.Security;

/// <summary>One accepted agent token, stored only as a hash.</summary>
/// <remarks>
/// With <see cref="AnyScope"/> the token is an enrollment token: any agent that holds it registers itself under the
/// scope name it announces, so the controller needs no list of agents. A token bound to one scope is stricter: its
/// holders can only report as that scope.
/// </remarks>
/// <param name="ScopeName">The one scope an agent presenting this token may report as, or <see cref="AnyScope"/>.</param>
/// <param name="TokenHash">SHA-256 of the token as hexadecimal text, as produced by <see cref="AgentTokenHash.Compute"/>.</param>
public sealed record AgentTokenEntry(string ScopeName, string TokenHash)
{
    /// <summary>Scope name that lets the agent choose its own scope.</summary>
    public const string AnyScope = "*";

    /// <summary>Whether agents holding this token choose their own scope.</summary>
    public bool AllowsAnyScope => ScopeName == AnyScope;
}
