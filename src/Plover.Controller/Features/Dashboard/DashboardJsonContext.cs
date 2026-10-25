using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Dashboard;

/// <summary>Source-generated JSON for the dashboard API, which keeps it free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Overview))]
[JsonSerializable(typeof(ScopeDetail))]
[JsonSerializable(typeof(Topology.TopologyGraph))]
internal sealed partial class DashboardJsonContext : JsonSerializerContext;
