namespace Plover.Common;

/// <summary>An address written as <c>host:port</c>, such as that of a controller replica or an agent.</summary>
public static class HostPort
{
    /// <summary>Splits an address into host and port.</summary>
    /// <param name="address">Text such as <c>controller.example:9440</c> or <c>[::1]:9440</c>.</param>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The port.</param>
    /// <returns><see langword="true"/> when the text has a host and an explicit port.</returns>
    public static bool TryParse(string? address, out string host, out int port)
    {
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate($"tcp://{address.Trim()}", UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.IsDefaultPort || uri.Port is < 1 or > 65535 || string.IsNullOrEmpty(uri.DnsSafeHost) || uri.PathAndQuery != "/")
        {
            return false;
        }

        host = uri.DnsSafeHost;
        port = uri.Port;
        return true;
    }
}
