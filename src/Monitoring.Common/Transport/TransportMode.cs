namespace Monitoring.Common.Transport;

/// <summary>How a connection between an agent and a controller is protected.</summary>
public enum TransportMode : byte
{
    /// <summary>Plain TCP. Only for a closed network where the traffic cannot leave it, because tokens then travel in clear text.</summary>
    None,

    /// <summary>The controller has a server certificate, which the agent checks against the certificate authority it was given.</summary>
    Tls,

    /// <summary>Like <see cref="Tls"/>, and the agent also proves itself with a client certificate that the controller checks.</summary>
    Mtls,
}
