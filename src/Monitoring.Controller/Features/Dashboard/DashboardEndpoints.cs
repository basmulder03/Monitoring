using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Maps the dashboard's read API.</summary>
public static class DashboardEndpoints
{
    /// <summary>Maps <c>GET /api/overview</c>, <c>GET /api/scopes/{name}</c> and the live stream <c>GET /api/events</c>.</summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/overview", (DashboardModel model) =>
            Results.Json(model.BuildOverview(), DashboardJsonContext.Default.Overview));

        endpoints.MapGet("/api/scopes/{name}", (string name, DashboardModel model) =>
            model.BuildScope(name) is { } scope
                ? Results.Json(scope, DashboardJsonContext.Default.ScopeDetail)
                : Results.NotFound());

        endpoints.MapGet("/api/events", (HttpContext context, OverviewStream stream) =>
            stream.StreamAsync(context.Response, context.RequestAborted));

        return endpoints;
    }
}
