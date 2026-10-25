using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Alerting;

/// <summary>What an operator asks for when making a silence. Either <c>until</c> or <c>minutes</c> says how long.</summary>
/// <param name="Rule">The rule to silence, or <c>*</c>.</param>
/// <param name="SubjectPrefix">What the subject has to start with.</param>
/// <param name="Minutes">How long from now.</param>
/// <param name="Until">When it ends, instead of <paramref name="Minutes"/>.</param>
/// <param name="Reason">Why.</param>
public sealed record SilenceRequest(string? Rule, string? SubjectPrefix, int? Minutes, DateTimeOffset? Until, string? Reason);

/// <summary>What an operator asks for when acknowledging an alert.</summary>
/// <param name="Key">The key of the alert.</param>
/// <param name="Note">What they want to say about it.</param>
public sealed record AcknowledgeRequest(string? Key, string? Note);

/// <summary>Source-generated JSON for the actions of operators.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ActionsExport))]
[JsonSerializable(typeof(List<RuntimeSilence>))]
[JsonSerializable(typeof(RuntimeSilence))]
[JsonSerializable(typeof(SilenceRequest))]
[JsonSerializable(typeof(AcknowledgeRequest))]
[JsonSerializable(typeof(List<AlertOccurrence>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class AlertActionsJsonContext : JsonSerializerContext;
