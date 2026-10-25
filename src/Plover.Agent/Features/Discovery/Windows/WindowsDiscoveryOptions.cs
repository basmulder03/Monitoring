namespace Plover.Agent.Features.Discovery;

/// <summary>Settings of the discovery of Windows services.</summary>
public sealed class WindowsDiscoveryOptions
{
    /// <summary>
    /// The services to show, each an exact name or a prefix ending in <c>*</c>, compared without regard to case. Nothing else on the machine is shown,
    /// because a server runs hundreds of services and only a few matter.
    /// </summary>
    public List<string> Services { get; set; } = [];

    /// <summary>How often the machine and the processes of the listed services are measured; slower than the services are asked, because the figures change all the time.</summary>
    public TimeSpan MeasureInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the Service Control Manager is asked.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
