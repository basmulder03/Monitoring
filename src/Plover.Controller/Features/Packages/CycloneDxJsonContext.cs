using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Packages;

/// <summary>Source-generated JSON for the bill of materials: camel-case names, and no property that has no value.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CycloneDxBom))]
[JsonSerializable(typeof(PackageCatalog))]
[JsonSerializable(typeof(PackageEntry))]
internal sealed partial class PackagesJsonContext : JsonSerializerContext;
