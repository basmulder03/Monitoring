using System.Text.Json.Serialization;

namespace Monitoring.Controller.Features.Vulnerabilities;

/// <summary>Source-generated JSON for the vulnerability findings.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VulnerabilityReport))]
internal sealed partial class VulnerabilitiesJsonContext : JsonSerializerContext;
