using System.Text.Json.Serialization;

namespace Monitoring.Controller.Features.SelfMonitoring;

/// <summary>Source-generated JSON for the system API, which keeps it free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SystemStatus))]
internal sealed partial class SystemJsonContext : JsonSerializerContext;
