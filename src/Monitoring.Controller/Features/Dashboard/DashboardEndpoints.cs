using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Monitoring.Controller.Features.Topology;
using Monitoring.Ui;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Maps the dashboard's read API.</summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Maps the read API (<c>GET /api/overview</c>, <c>GET /api/scopes/{name}</c>, <c>GET /api/topology</c>), the live stream (<c>GET /api/events</c>)
    /// and the page itself (<c>/</c>, <c>/tv</c> for a wall display, and its script and style sheet).
    /// </summary>
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

        endpoints.MapGet("/api/topology", (ITopology topology) =>
            Results.Json(topology.GetGraph(), DashboardJsonContext.Default.TopologyGraph));

        endpoints.MapGet("/api/events", (HttpContext context, OverviewStream stream) =>
            stream.StreamAsync(context.Response, context.RequestAborted));

        endpoints.MapGet("/", () => Asset("index.html"));
        endpoints.MapGet("/tv", () => Asset("index.html"));
        endpoints.MapGet("/app.js", () => Asset("app.js"));
        endpoints.MapGet("/styles.css", () => Asset("styles.css"));

        return endpoints;
    }

    private static IResult Asset(string path)
    {
        var stream = UiAssets.Open(path);
        return stream is null ? Results.NotFound() : new AssetResult(stream, UiAssets.ContentTypeOf(path)!);
    }

    /// <summary>Serves an embedded file with headers that keep the page from loading anything but its own files.</summary>
    private sealed class AssetResult(Stream content, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await using (content)
            {
                var headers = httpContext.Response.Headers;
                headers.ContentType = contentType;
                headers.CacheControl = "no-cache";
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Content-Security-Policy"] =
                    "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                await content.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
        }
    }
}
