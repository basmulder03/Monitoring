using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.WebAuth;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>
/// The only routes that change something. They need a person who signed in with the role of operator, so they do nothing on a dashboard that needs no sign-in,
/// and a request has to carry the header <c>X-Monitoring-Action</c>, which a page of another site cannot add without the controller's say-so (there is no CORS).
/// </summary>
public static class AlertActionsEndpoints
{
    /// <summary>The header every request that changes something carries.</summary>
    public const string ActionHeader = "X-Monitoring-Action";

    /// <summary>Maps <c>/api/silences</c>, <c>/api/alerts/acknowledge</c> and <c>/api/actions</c>.</summary>
    /// <param name="endpoints">The endpoints to extend.</param>
    /// <returns>The same endpoints, for chaining.</returns>
    public static IEndpointRouteBuilder MapAlertActions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/silences", (AlertActions actions) =>
            Results.Text(JsonSerializer.Serialize(actions.Silences().ToList(), AlertActionsJsonContext.Default.ListRuntimeSilence), "application/json"));

        endpoints.MapGet("/api/alerts/history", (HttpContext context, AlertHistory history) =>
            Results.Text(JsonSerializer.Serialize(history.Recent(int.TryParse(context.Request.Query["limit"], out var limit) ? limit : 50).ToList(), AlertActionsJsonContext.Default.ListAlertOccurrence), "application/json"));

        // What another replica asks for to show the same as this one.
        endpoints.MapGet("/api/actions", (AlertActions actions) =>
            Results.Text(JsonSerializer.Serialize(actions.Export(), AlertActionsJsonContext.Default.ActionsExport), "application/json"));

        endpoints.MapPost("/api/silences", async (HttpContext context, AlertActions actions, TimeProvider time) =>
        {
            if (Refuse(context) is { } refused)
            {
                return refused;
            }

            var request = await ReadAsync(context, AlertActionsJsonContext.Default.SilenceRequest).ConfigureAwait(false);
            if (request is null || (request.Minutes is null) == (request.Until is null))
            {
                return Error(StatusCodes.Status400BadRequest, "Say how long with either minutes or until.");
            }

            var until = request.Until ?? time.GetUtcNow().AddMinutes(request.Minutes!.Value);
            try
            {
                var silence = actions.Silence(request.Rule ?? "*", request.SubjectPrefix ?? "", until, request.Reason, UserName(context));
                return Results.Text(JsonSerializer.Serialize(silence, AlertActionsJsonContext.Default.RuntimeSilence), "application/json", statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Error(StatusCodes.Status400BadRequest, ex.Message);
            }
        });

        endpoints.MapDelete("/api/silences/{id}", (HttpContext context, string id, AlertActions actions) =>
            Refuse(context) ?? (actions.RemoveSilence(id, UserName(context)) ? Results.NoContent() : Error(StatusCodes.Status404NotFound, "There is no such silence.")));

        endpoints.MapPost("/api/alerts/acknowledge", async (HttpContext context, AlertActions actions) =>
        {
            if (Refuse(context) is { } refused)
            {
                return refused;
            }

            var request = await ReadAsync(context, AlertActionsJsonContext.Default.AcknowledgeRequest).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(request?.Key))
            {
                return Error(StatusCodes.Status400BadRequest, "Say which alert, by its key.");
            }

            actions.Acknowledge(request.Key, UserName(context), request.Note);
            return Results.NoContent();
        });

        endpoints.MapDelete("/api/alerts/acknowledge", (HttpContext context, AlertActions actions) =>
            Refuse(context) ?? (actions.RemoveAcknowledgement(context.Request.Query["key"].ToString(), UserName(context)) ? Results.NoContent() : Error(StatusCodes.Status404NotFound, "That alert is not acknowledged.")));

        return endpoints;
    }

    /// <summary>Why a request may not change anything, or <see langword="null"/> when it may.</summary>
    private static IResult? Refuse(HttpContext context)
    {
        if (context.Items[typeof(WebUser)] is not WebUser user)
        {
            return Error(StatusCodes.Status403Forbidden, "Changing alerts needs authentication of the dashboard to be turned on (Controller:Web:Auth) and a person who is signed in.");
        }

        if (user.Role != WebRole.Operator)
        {
            return Error(StatusCodes.Status403Forbidden, "Only an operator can change alerts.");
        }

        return context.Request.Headers.ContainsKey(ActionHeader)
            ? null
            : Error(StatusCodes.Status403Forbidden, $"A request that changes something carries the header {ActionHeader}.");
    }

    private static string UserName(HttpContext context) => (context.Items[typeof(WebUser)] as WebUser)?.Name ?? "unknown";

    private static IResult Error(int status, string message) =>
        Results.Text(JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = message }, AlertActionsJsonContext.Default.DictionaryStringString), "application/json", statusCode: status);

    private static async Task<T?> ReadAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        if (context.Request.ContentLength is > 4096)
        {
            return default;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync(context.Request.Body, info, context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
