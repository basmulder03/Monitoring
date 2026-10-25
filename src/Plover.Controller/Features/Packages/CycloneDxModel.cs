using System.Text.Json.Serialization;

namespace Plover.Controller.Features.Packages;

/// <summary>A software bill of materials in the CycloneDX 1.5 JSON format, which tools for vulnerability scanning and licence checks read.</summary>
/// <param name="BomFormat">Always <c>CycloneDX</c>.</param>
/// <param name="SpecVersion">The version of the format.</param>
/// <param name="Version">The version of this document; always 1, because nothing is stored.</param>
/// <param name="SerialNumber">A URN that identifies the document.</param>
/// <param name="Metadata">When it was made and what it describes.</param>
/// <param name="Components">The packages.</param>
public sealed record CycloneDxBom(
    string BomFormat,
    string SpecVersion,
    int Version,
    string SerialNumber,
    CycloneDxMetadata Metadata,
    IReadOnlyList<CycloneDxComponent> Components);

/// <summary>When a bill of materials was made and what it describes.</summary>
/// <param name="Timestamp">When, in UTC, as ISO 8601.</param>
/// <param name="Component">The thing the packages belong to: a service, or the whole landscape.</param>
public sealed record CycloneDxMetadata(string Timestamp, CycloneDxComponent Component);

/// <summary>A component of a bill of materials.</summary>
/// <param name="Type"><c>application</c> for a service, <c>library</c> for a package.</param>
/// <param name="Name">The name.</param>
/// <param name="Version">The version, if known.</param>
/// <param name="BomRef">An id that is unique within the document.</param>
/// <param name="Purl">The package URL, such as <c>pkg:nuget/Serilog@4.0.0</c>, which scanners use to match advisories.</param>
/// <param name="Scope"><c>required</c> for a package that is built in.</param>
public sealed record CycloneDxComponent(
    string Type,
    string Name,
    string? Version,
    [property: JsonPropertyName("bom-ref")] string BomRef,
    string? Purl = null,
    string? Scope = null);
