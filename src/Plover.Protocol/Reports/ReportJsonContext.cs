using System.Text.Json.Serialization;

namespace Plover.Protocol.Reports;

/// <summary>Source-generated JSON for the reports an application sends, which keeps them free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(InstanceDescription))]
[JsonSerializable(typeof(HealthReport))]
[JsonSerializable(typeof(MetricsReport))]
[JsonSerializable(typeof(EdgesReport))]
[JsonSerializable(typeof(SpansReport))]
[JsonSerializable(typeof(PackagesReport))]
[JsonSerializable(typeof(MetricHistory))]
public sealed partial class ReportJsonContext : JsonSerializerContext;
