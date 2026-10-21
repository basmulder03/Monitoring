using System.Text.Json.Serialization;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Source-generated JSON for advisories in the OSV format (snake case names); what the controller does not need is ignored.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(OsvAdvisory))]
[JsonSerializable(typeof(List<OsvAdvisory>))]
internal sealed partial class OsvJsonContext : JsonSerializerContext;
