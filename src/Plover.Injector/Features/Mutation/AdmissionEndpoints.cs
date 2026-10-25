using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Plover.Injector.Features.Mutation;

/// <summary>Maps the endpoints of the webhook.</summary>
public static class AdmissionEndpoints
{
    /// <summary>Maps <c>POST /mutate</c> and <c>POST /mutate/namespace</c>, which answer an admission review for a pod, and <c>GET /healthz</c>.</summary>
    /// <param name="endpoints">The route builder to add the endpoints to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAdmission(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Text("ok"));
        // Two paths, because a registration can only name a path and not a query: the one for pods that asked themselves, and the one for namespaces that asked.
        endpoints.MapPost("/mutate", (HttpRequest request, PodMutator mutator, ILoggerFactory logs) => AnswerAsync(request, mutator, fromNamespace: false, logs));
        endpoints.MapPost("/mutate/namespace", (HttpRequest request, PodMutator mutator, ILoggerFactory logs) => AnswerAsync(request, mutator, fromNamespace: true, logs));
        return endpoints;
    }

    private static async Task<IResult> AnswerAsync(HttpRequest request, PodMutator mutator, bool fromNamespace, ILoggerFactory logs)
    {
        var review = await ReadAsync(request).ConfigureAwait(false);
        return review is null ? Results.BadRequest() : Results.Text(Answer(review, mutator, fromNamespace, logs.CreateLogger("Plover.Injector")), "application/json");
    }

    private static async Task<JsonObject?> ReadAsync(HttpRequest request)
    {
        try
        {
            // An admission review is small; anything large is not one.
            var body = new MemoryStream();
            await request.Body.CopyToAsync(body, request.HttpContext.RequestAborted).ConfigureAwait(false);
            return body.Length is > 0 and <= 4 * 1024 * 1024 ? JsonNode.Parse(body.ToArray()) as JsonObject : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Answers a review: always allowed, because a Plover sidecar must never stop a pod from starting, with a patch when there is something to add.</summary>
    internal static string Answer(JsonObject review, PodMutator mutator, bool fromNamespace, ILogger logger)
    {
        var request = review["request"] as JsonObject;
        var uid = request?["uid"]?.GetValue<string>() ?? "";
        var response = new JsonObject { ["uid"] = uid, ["allowed"] = true };

        try
        {
            if (request?["object"] is JsonObject pod)
            {
                var patch = mutator.Mutate(pod, fromNamespace);
                if (patch.Count > 0)
                {
                    response["patchType"] = "JSONPatch";
                    response["patch"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(patch.ToJsonString()));
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            // A pod that cannot be understood is let through as it is.
            logger.LogWarning("Letting a pod through without the sidecar: {Reason}", ex.Message);
        }

        return new JsonObject { ["apiVersion"] = "admission.k8s.io/v1", ["kind"] = "AdmissionReview", ["response"] = response }.ToJsonString();
    }
}
