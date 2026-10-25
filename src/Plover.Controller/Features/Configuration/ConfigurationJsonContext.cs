using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Configuration;

/// <summary>Source-generated JSON for the central configuration file. Unknown names are an error, so a typo is not silently ignored.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ConfigurationFileModel))]
internal sealed partial class ConfigurationJsonContext : JsonSerializerContext;
