using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Monitoring.Controller.Features.WebAuth;

/// <summary>Who is looking at the dashboard.</summary>
/// <param name="Name">The name of the person or token.</param>
/// <param name="Role">What they may do.</param>
/// <param name="ViaToken">Whether a token identified them, as opposed to a proxy.</param>
public sealed record WebUser(string Name, WebRole Role, bool ViaToken = false);

/// <summary>
/// Decides who sent a request: a trusted proxy that says so in a header, or a token. The proxy comes first, and only counts when the request really comes from one of the addresses that
/// were configured, because anyone else could write the same header. A token is compared as a hash, in constant time, against every entry.
/// </summary>
internal sealed class WebAuthenticator
{
    /// <summary>The name of the cookie that remembers a token that was given in the address, so a page and its live stream do not need it again.</summary>
    public const string CookieName = "monitoring_token";

    private readonly WebAuthOptions _options;
    private readonly List<(IPAddress Network, int Bits)> _proxies;
    private readonly List<(WebToken Token, byte[] Hash)> _tokens;

    /// <summary>Creates the authenticator from the settings.</summary>
    /// <param name="options">The proxies and tokens.</param>
    /// <exception cref="ArgumentException">A proxy is not an address or network, or a token hash is not 64 hexadecimal characters.</exception>
    public WebAuthenticator(WebAuthOptions options)
    {
        _options = options;
        _proxies = [.. options.TrustedProxies.Select(ParseNetwork)];
        _tokens = [.. options.Tokens.Select(token => (token, ParseHash(token)))];
    }

    /// <summary>Whether people must be identified at all.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>Identifies the sender of a request.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Who it is, or <see langword="null"/> when nobody could be identified.</returns>
    public WebUser? Identify(HttpRequest request)
    {
        if (TrustedProxy(request.HttpContext.Connection.RemoteIpAddress)
            && request.Headers[_options.UserHeader].ToString() is { Length: > 0 } name)
        {
            var groups = request.Headers[_options.GroupsHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var operatorRole = !string.IsNullOrEmpty(_options.OperatorGroup) && groups.Contains(_options.OperatorGroup, StringComparer.Ordinal);
            return new WebUser(name, operatorRole ? WebRole.Operator : WebRole.Viewer);
        }

        return TokenOf(request) is { Length: > 0 } presented ? MatchToken(presented) : null;
    }

    private static string? TokenOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        if (request.Cookies.TryGetValue(CookieName, out var cookie))
        {
            return cookie;
        }

        return request.Query["token"].ToString();
    }

    private WebUser? MatchToken(string presented)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        WebUser? found = null;

        // Every entry is compared, so the time taken does not tell which one matched.
        foreach (var (token, expected) in _tokens)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, expected))
            {
                found ??= new WebUser(token.Name, token.Role, ViaToken: true);
            }
        }

        return found;
    }

    private bool TrustedProxy(IPAddress? remote)
    {
        if (remote is null)
        {
            return false;
        }

        remote = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return _proxies.Any(proxy => proxy.Network.AddressFamily == remote.AddressFamily && Within(remote, proxy.Network, proxy.Bits));
    }

    private static bool Within(IPAddress address, IPAddress network, int bits)
    {
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        for (var bit = 0; bit < bits; bit += 8)
        {
            var mask = (byte)(bits - bit >= 8 ? 0xFF : 0xFF << (8 - (bits - bit)));
            if ((a[bit / 8] & mask) != (n[bit / 8] & mask))
            {
                return false;
            }
        }

        return true;
    }

    private static (IPAddress, int) ParseNetwork(string text)
    {
        var parts = text.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address))
        {
            throw new ArgumentException($"A trusted proxy must be an IP address or a network such as 10.0.0.0/8, but is '{text}'.", nameof(text));
        }

        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var bits = parts.Length == 2 ? (int.TryParse(parts[1], out var parsed) && parsed >= 0 && parsed <= max ? parsed : throw new ArgumentException($"'{text}' has no valid prefix length.", nameof(text))) : max;
        return (address, bits);
    }

    private static byte[] ParseHash(WebToken token)
    {
        if (string.IsNullOrWhiteSpace(token.Name) || token.Hash is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException($"A dashboard token needs a name and a hash of 64 hexadecimal characters (token '{token.Name}').", nameof(token));
        }

        return Convert.FromHexString(hash);
    }
}
