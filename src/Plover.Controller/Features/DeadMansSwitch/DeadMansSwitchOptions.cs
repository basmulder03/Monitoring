namespace Plover.Controller.Features.DeadMansSwitch;

/// <summary>Settings of the dead-man's switch.</summary>
public sealed class DeadMansSwitchOptions
{
    /// <summary>Gets or sets the address of the external watchdog that is pinged; <see langword="null"/> turns the ping off.</summary>
    public Uri? Url { get; set; }

    /// <summary>Gets or sets how often the watchdog is pinged.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}
