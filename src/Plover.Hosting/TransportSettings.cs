using Plover.Common.Transport;

namespace Plover.Hosting;

/// <summary>Reads the transport security settings, which the controller and the agent spell the same way.</summary>
public static class TransportSettings
{
    /// <summary>Reads the settings of a <c>Transport</c> section.</summary>
    /// <param name="transport">The section; every setting is optional and plain TCP is the default.</param>
    /// <returns>The options, not yet checked: the executable's slice does that when it is registered.</returns>
    /// <exception cref="ConfigurationException">The mode is not <c>none</c>, <c>tls</c> or <c>mtls</c>, or a duration is invalid.</exception>
    public static TransportSecurityOptions Read(SettingsReader transport)
    {
        var defaults = new TransportSecurityOptions();
        return new TransportSecurityOptions
        {
            Mode = transport.Text("Mode", "none").ToLowerInvariant() switch
            {
                "none" => TransportMode.None,
                "tls" => TransportMode.Tls,
                "mtls" => TransportMode.Mtls,
                var other => throw new ConfigurationException($"Setting '{transport.NameOf("Mode")}' must be 'none', 'tls' or 'mtls', but is '{other}'."),
            },
            CertificateFile = transport.Optional("Certificate"),
            KeyFile = transport.Optional("Key"),
            CaFile = transport.Optional("CaFile"),
            ClientCaFile = transport.Optional("ClientCaFile"),
            ServerName = transport.Optional("ServerName"),
            HandshakeTimeout = transport.Duration("HandshakeTimeout", defaults.HandshakeTimeout),
        };
    }
}
