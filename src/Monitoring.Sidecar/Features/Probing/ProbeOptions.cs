namespace Monitoring.Sidecar.Features.Probing;

/// <summary>Settings of the probing slice.</summary>
public sealed class ProbeOptions
{
    /// <summary>How often the application is asked for its health.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
}
