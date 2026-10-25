namespace Plover.Controller.Features.ChangeMarkers;

/// <summary>The recent changes of version, as this controller replica noticed them.</summary>
/// <remarks>Nothing is persisted: a restarted replica starts with an empty list, and a version already running then is not a change. All members are safe to call from several threads.</remarks>
public interface IChangeMarkers
{
    /// <summary>Gets the markers, newest first.</summary>
    /// <returns>The markers.</returns>
    IReadOnlyList<ChangeMarker> GetRecent();
}
