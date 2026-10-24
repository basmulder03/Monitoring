using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Monitoring.Controller.Features.WebAuth;

/// <summary>Who is signed in, as the dashboard asks for it.</summary>
/// <param name="AuthenticationEnabled">Whether the dashboard asks people who they are at all.</param>
/// <param name="Name">The name of the person or token; empty when nobody is asked.</param>
/// <param name="Role"><c>viewer</c> or <c>operator</c>.</param>
public sealed record WhoAmI(bool AuthenticationEnabled, string Name, string Role);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WhoAmI))]
internal sealed partial class WebAuthJsonContext : JsonSerializerContext;

/// <summary>Registers and applies the authentication of the dashboard.</summary>
public static class WebAuthSlice
{
    /// <summary>Adds the authentication. It does nothing to a request until a proxy is trusted or a token is configured.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="WebAuthOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">A proxy or a token is not valid.</exception>
    public static IServiceCollection AddWebAuth(this IServiceCollection services, Action<WebAuthOptions> configure)
    {
        var options = new WebAuthOptions();
        configure(options);
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(new WebAuthenticator(options));
        return services;
    }

    /// <summary>
    /// Makes every request prove who it is before anything is served, except <c>/healthz</c>, which the platform asks for. A token in the address (<c>?token=</c>) is turned into a cookie, so a wall display
    /// opens its page once and the live stream and the data that follow carry it. Adds <c>/healthz</c> and <c>/api/me</c>. Call it before mapping the endpoints.
    /// </summary>
    /// <param name="app">The application to extend.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseWebAuth(this WebApplication app)
    {
        var authenticator = app.Services.GetRequiredService<WebAuthenticator>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Monitoring.Controller.WebAuth");

        app.Use(async (context, next) =>
        {
            if (!authenticator.Enabled || context.Request.Path == "/healthz")
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            var user = authenticator.Identify(context.Request);
            if (user is null)
            {
                logger.LogWarning("Refused {Method} {Path} from {Address}: not signed in", context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await context.Response.WriteAsync("Sign in to see the dashboard.").ConfigureAwait(false);
                return;
            }

            // A token given in the address is kept in a cookie that script cannot read, and is not sent to other sites.
            if (user.ViaToken && context.Request.Query["token"].ToString() is { Length: > 0 } given)
            {
                context.Response.Cookies.Append(WebAuthenticator.CookieName, given, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", Secure = context.Request.IsHttps });
            }

            context.Items[typeof(WebUser)] = user;
            await next(context).ConfigureAwait(false);
        });

        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/api/me", (HttpContext context) =>
        {
            var user = context.Items[typeof(WebUser)] as WebUser;
            return Results.Json(new WhoAmI(authenticator.Enabled, user?.Name ?? "", (user?.Role ?? WebRole.Operator).ToString().ToLowerInvariant()), WebAuthJsonContext.Default.WhoAmI);
        });
        return app;
    }
}
