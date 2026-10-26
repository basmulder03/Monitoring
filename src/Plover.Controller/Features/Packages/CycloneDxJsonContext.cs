using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Packages;

/// <summary>Source-generated JSON for the bill of materials: camel-case names, and no property that has no value.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CycloneDxBom))]
internal sealed partial class PackagesJsonContext : JsonSerializerContext;

/// <summary>
/// Source-generated JSON for the packages API of the dashboard. Unlike the bill of materials it keeps properties that have no value, because the TypeScript
/// contract (generated from these types) says a package without a violation has <c>violation: null</c>, not no property at all.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PackageCatalog))]
[JsonSerializable(typeof(PackageEntry))]
internal sealed partial class PackagesApiJsonContext : JsonSerializerContext;
