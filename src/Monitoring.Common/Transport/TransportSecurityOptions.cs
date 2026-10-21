namespace Monitoring.Common.Transport;

/// <summary>Settings of the transport security of one side of the agent-to-controller connection.</summary>
/// <remarks>The controller uses the certificate settings and <see cref="ClientCaFile"/>; the agent uses the others. Files are PEM.</remarks>
public sealed class TransportSecurityOptions
{
    /// <summary>How connections are protected; the same on both sides.</summary>
    public TransportMode Mode { get; set; } = TransportMode.None;

    /// <summary>The certificate, with its chain, that this side presents: the server certificate on a controller, the client certificate on an agent in <see cref="TransportMode.Mtls"/>.</summary>
    public string? CertificateFile { get; set; }

    /// <summary>The private key of <see cref="CertificateFile"/>. May be omitted when the certificate file holds the key as well.</summary>
    public string? KeyFile { get; set; }

    /// <summary>On an agent: the certificate authority that signed the controller's certificate. Without it the certificate authorities of the machine are trusted.</summary>
    public string? CaFile { get; set; }

    /// <summary>On a controller in <see cref="TransportMode.Mtls"/>: the certificate authority that signs the certificates of agents.</summary>
    public string? ClientCaFile { get; set; }

    /// <summary>On an agent: the name to check the controller's certificate against, when it differs from the host name in the address, for example behind a Route.</summary>
    public string? ServerName { get; set; }

    /// <summary>How long the TLS handshake may take before the connection is dropped.</summary>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
