using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plover.Controller.Features.ChangeMarkers;

/// <summary>Maps the read API of the change markers.</summary>
public static class ChangeMarkersEndpoints
{
    /// <summary>Maps <c>GET /api/changes</c>.</summary>
    /// <param name="endpoints">The route builder to add the endpoint to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapChangeMarkers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/changes", (IChangeMarkers markers) =>
            Results.Json(markers.GetRecent(), ChangeMarkersJsonContext.Default.IReadOnlyListChangeMarker));
        return endpoints;
    }
}
