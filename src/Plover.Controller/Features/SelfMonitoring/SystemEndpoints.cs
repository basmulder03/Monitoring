using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plover.Controller.Features.SelfMonitoring;

/// <summary>Maps the read API of the state of this replica.</summary>
public static class SystemEndpoints
{
    /// <summary>Maps <c>GET /api/system</c>.</summary>
    /// <param name="endpoints">The route builder to add the endpoint to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSelfMonitoring(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/system", (SystemTelemetry telemetry) =>
            Results.Json(telemetry.Read(), SystemJsonContext.Default.SystemStatus));
        return endpoints;
    }
}
