namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Settings of the dashboard slice.</summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// How often the state is checked for changes to push to open pages. Changes are coalesced into at most one update per interval,
    /// so a burst of changes does not flood the pages.
    /// </summary>
    public TimeSpan PushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a connection may stay silent before a keep-alive comment is sent, so proxies do not close it.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}
