using System.Text.Json.Serialization;
using Monitoring.Protocol.Messages;

namespace Monitoring.Protocol.Serialization;

/// <summary>Source-generated JSON metadata for all wire messages; keeps serialization free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Message))]
public sealed partial class ProtocolJsonContext : JsonSerializerContext;
