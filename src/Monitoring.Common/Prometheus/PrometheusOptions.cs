namespace Monitoring.Common.Prometheus;

/// <summary>Settings of the optional Prometheus export.</summary>
public sealed class PrometheusOptions
{
    /// <summary>Gets or sets the port to serve <c>/metrics</c> on; 0 turns the export off, which is the default.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the IP address to listen on; local only unless changed.</summary>
    public string Address { get; set; } = "127.0.0.1";
}
