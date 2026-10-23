namespace Monitoring.Agent.Features.Discovery;

/// <summary>Settings of the discovery slice.</summary>
public sealed class DiscoveryOptions
{
    /// <summary>Where instances are discovered; <see cref="DiscoveryKind.None"/> by default.</summary>
    public DiscoveryKind Kind { get; set; } = DiscoveryKind.None;

    /// <summary>Settings used when <see cref="Kind"/> is <see cref="DiscoveryKind.Kubernetes"/>.</summary>
    public KubernetesDiscoveryOptions Kubernetes { get; set; } = new();

    /// <summary>Settings used when <see cref="Kind"/> is <see cref="DiscoveryKind.Windows"/>.</summary>
    public WindowsDiscoveryOptions Windows { get; set; } = new();
}
