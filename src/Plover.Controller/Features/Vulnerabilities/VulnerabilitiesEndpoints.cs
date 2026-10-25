using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plover.Controller.Features.Vulnerabilities;

/// <summary>Maps the read API of the vulnerability findings.</summary>
public static class VulnerabilitiesEndpoints
{
    /// <summary>Maps <c>GET /api/vulnerabilities</c>.</summary>
    /// <param name="endpoints">The route builder to add the endpoint to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapVulnerabilities(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/vulnerabilities", (IVulnerabilities vulnerabilities) =>
            Results.Json(vulnerabilities.Check(), VulnerabilitiesJsonContext.Default.VulnerabilityReport));
        return endpoints;
    }
}
