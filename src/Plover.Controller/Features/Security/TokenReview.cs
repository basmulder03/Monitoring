using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Security;

/// <summary>The request to the API server to check a token, and its answer (<c>authentication.k8s.io/v1</c>).</summary>
/// <param name="ApiVersion">Always <c>authentication.k8s.io/v1</c>.</param>
/// <param name="Kind">Always <c>TokenReview</c>.</param>
/// <param name="Spec">What to check; set in a request.</param>
/// <param name="Status">What the API server found; set in an answer.</param>
internal sealed record TokenReview(string ApiVersion = "authentication.k8s.io/v1", string Kind = "TokenReview", TokenReviewSpec? Spec = null, TokenReviewStatus? Status = null);

/// <summary>What to check.</summary>
/// <param name="Token">The token the agent presented.</param>
/// <param name="Audiences">The audiences the token must be made for.</param>
internal sealed record TokenReviewSpec(string Token, string[] Audiences);

/// <summary>What the API server found.</summary>
/// <param name="Authenticated">Whether the token is a valid one.</param>
/// <param name="User">Who the token belongs to.</param>
/// <param name="Audiences">The audiences of the token that match the ones asked for.</param>
/// <param name="Error">Why the token was not accepted, when it was not.</param>
internal sealed record TokenReviewStatus(bool Authenticated = false, TokenReviewUser? User = null, string[]? Audiences = null, string? Error = null);

/// <summary>Who a token belongs to.</summary>
/// <param name="Username">For a service account, <c>system:serviceaccount:{namespace}:{name}</c>.</param>
internal sealed record TokenReviewUser(string? Username = null);

/// <summary>Source-generated JSON for the token review, which keeps it free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenReview))]
internal sealed partial class SecurityJsonContext : JsonSerializerContext;
