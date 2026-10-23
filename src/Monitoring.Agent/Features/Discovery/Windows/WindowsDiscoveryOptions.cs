namespace Monitoring.Agent.Features.Discovery;

/// <summary>Settings of the discovery of Windows services.</summary>
public sealed class WindowsDiscoveryOptions
{
    /// <summary>
    /// The services to show, each an exact name or a prefix ending in <c>*</c>, compared without regard to case. Nothing else on the machine is shown,
    /// because a server runs hundreds of services and only a few matter.
    /// </summary>
    public List<string> Services { get; set; } = [];

    /// <summary>How often the Service Control Manager is asked.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
