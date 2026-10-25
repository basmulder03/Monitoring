namespace Plover.Controller.Features.WebAuth;

/// <summary>What a signed-in person may do.</summary>
public enum WebRole
{
    /// <summary>May look at everything.</summary>
    Viewer,

    /// <summary>May look at everything, and change what the dashboard can change (nothing yet).</summary>
    Operator,
}

/// <summary>A long-lived token for a person or a wall display, of which only the hash is kept.</summary>
/// <param name="Name">Who it is for, as shown and logged.</param>
/// <param name="Hash">The SHA-256 of the token, as 64 hexadecimal characters.</param>
/// <param name="Role">What the token allows.</param>
public sealed record WebToken(string Name, string Hash, WebRole Role = WebRole.Viewer);

/// <summary>Settings of the authentication of the dashboard.</summary>
public sealed class WebAuthOptions
{
    /// <summary>The proxies whose word is believed, as IP addresses or networks such as <c>10.0.0.0/8</c>. Empty: no proxy is trusted.</summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>The header in which a trusted proxy puts the name of the person it has signed in.</summary>
    public string UserHeader { get; set; } = "X-Forwarded-User";

    /// <summary>The header in which a trusted proxy puts the groups of that person, separated by commas.</summary>
    public string GroupsHeader { get; set; } = "X-Forwarded-Groups";

    /// <summary>A person who belongs to this group is an operator; everyone else a viewer. Empty: everyone a proxy signs in is a viewer.</summary>
    public string? OperatorGroup { get; set; }

    /// <summary>The tokens that may be used instead of, or beside, a proxy.</summary>
    public List<WebToken> Tokens { get; set; } = [];

    /// <summary>Whether the dashboard asks who is looking: when a proxy is trusted or a token is configured.</summary>
    public bool Enabled => TrustedProxies.Count > 0 || Tokens.Count > 0;
}
