namespace Monitoring.Controller.Features.ChangeMarkers;

/// <summary>A version of a service that appeared where another one was running, which is often what a problem that follows is about.</summary>
/// <param name="At">When the controller noticed it.</param>
/// <param name="Scope">The scope of the service.</param>
/// <param name="Service">The service.</param>
/// <param name="From">The versions that were running before, joined with a comma.</param>
/// <param name="To">The version that appeared.</param>
public sealed record ChangeMarker(DateTimeOffset At, string Scope, string Service, string From, string To);
