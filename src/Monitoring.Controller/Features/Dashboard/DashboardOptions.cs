namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Settings of the dashboard slice.</summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// How often the state is checked for changes to push to open pages. Changes are coalesced into at most one update per interval,
    /// so a burst of changes does not flood the pages.
    /// </summary>
    public TimeSpan PushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a connection may stay silent before a keep-alive event is sent. It keeps proxies from closing the connection and
    /// tells the page the link is alive, so it must be well below the 15 seconds after which a page warns that its data is old.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(5);
}
