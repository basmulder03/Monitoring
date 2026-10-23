using System.Text.Json.Serialization;

namespace Monitoring.Controller.Features.ChangeMarkers;

/// <summary>Source-generated JSON for the change markers API, which keeps it free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<ChangeMarker>))]
internal sealed partial class ChangeMarkersJsonContext : JsonSerializerContext;
