namespace Plover.Common.Transport;

/// <summary>Creates the <see cref="ITransportSecurity"/> for a side of the connection from its settings.</summary>
public static class TransportSecurity
{
    /// <summary>Creates the security of the controller, which accepts connections.</summary>
    /// <param name="options">The settings.</param>
    /// <returns>The security for the mode.</returns>
    /// <exception cref="ArgumentException">A setting that the mode needs is missing, or a certificate file cannot be read or holds no certificate.</exception>
    public static ITransportSecurity ForServer(TransportSecurityOptions options) => Create(options, server: true);

    /// <summary>Creates the security of an agent, which opens connections.</summary>
    /// <param name="options">The settings.</param>
    /// <returns>The security for the mode.</returns>
    /// <exception cref="ArgumentException">A setting that the mode needs is missing, or a certificate file cannot be read or holds no certificate.</exception>
    public static ITransportSecurity ForClient(TransportSecurityOptions options) => Create(options, server: false);

    private static ITransportSecurity Create(TransportSecurityOptions options, bool server)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HandshakeTimeout, TimeSpan.Zero);
        return options.Mode switch
        {
            TransportMode.None => NoTransportSecurity.Instance,
            TransportMode.Tls or TransportMode.Mtls => new TlsTransportSecurity(options, server),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Mode, "Unknown transport mode."),
        };
    }
}
