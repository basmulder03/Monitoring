using System.Text.Json;

namespace Monitoring.Protocol.Reports;

/// <summary>Turns reports into the JSON carried by <see cref="Messages.PullResult"/> and back.</summary>
public static class ReportPayload
{
    /// <summary>Writes a description of an application.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(InstanceDescription description) =>
        JsonSerializer.Serialize(description, ReportJsonContext.Default.InstanceDescription);

    /// <summary>Writes a health report.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(HealthReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.HealthReport);

    /// <summary>Writes the metrics of an application.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(MetricsReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.MetricsReport);

    /// <summary>Writes the dependency edges of an application.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(EdgesReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.EdgesReport);

    /// <summary>Writes the recent spans of an application.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(SpansReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.SpansReport);

    /// <summary>Writes the packages of an application.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(PackagesReport report) =>
        JsonSerializer.Serialize(report, ReportJsonContext.Default.PackagesReport);

    /// <summary>Reads a description of an application.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The description, or <see langword="null"/> when the text is not one.</returns>
    public static InstanceDescription? ReadDescription(string? json) =>
        Read(json, ReportJsonContext.Default.InstanceDescription);

    /// <summary>Reads a health report.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The report, or <see langword="null"/> when the text is not one.</returns>
    public static HealthReport? ReadHealth(string? json) =>
        Read(json, ReportJsonContext.Default.HealthReport);

    /// <summary>Reads the metrics of an application.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The report, or <see langword="null"/> when the text is not one.</returns>
    public static MetricsReport? ReadMetrics(string? json) =>
        Read(json, ReportJsonContext.Default.MetricsReport);

    /// <summary>Reads the dependency edges of an application.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The report, or <see langword="null"/> when the text is not one.</returns>
    public static EdgesReport? ReadEdges(string? json) =>
        Read(json, ReportJsonContext.Default.EdgesReport);

    /// <summary>Reads the recent spans of an application.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The report, or <see langword="null"/> when the text is not one.</returns>
    public static SpansReport? ReadSpans(string? json) =>
        Read(json, ReportJsonContext.Default.SpansReport);

    /// <summary>Reads the packages of an application.</summary>
    /// <param name="json">The JSON text of a pull result.</param>
    /// <returns>The report, or <see langword="null"/> when the text is not one.</returns>
    public static PackagesReport? ReadPackages(string? json) =>
        Read(json, ReportJsonContext.Default.PackagesReport);

    private static T? Read<T>(string? json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            // Applications are not trusted to send well-formed reports; a bad one is the same as none.
            return null;
        }
    }
}
