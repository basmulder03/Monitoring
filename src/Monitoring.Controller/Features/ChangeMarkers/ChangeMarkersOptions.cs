namespace Monitoring.Controller.Features.ChangeMarkers;

/// <summary>Settings of the change markers slice.</summary>
public sealed class ChangeMarkersOptions
{
    /// <summary>How often the registry is read for new versions.</summary>
    public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many markers are kept; the oldest go first.</summary>
    public int MaxMarkers { get; set; } = 200;
}
