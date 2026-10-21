using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.LibraryLink;

/// <summary>Asks the application, through its monitoring client, about itself.</summary>
public interface ILibraryLink
{
    /// <summary>Asks the application who it is.</summary>
    /// <param name="ct">Cancels the question.</param>
    /// <returns>The service name and version, or <see langword="null"/> when the application cannot be reached or does not answer.</returns>
    Task<InstanceDescription?> GetDescriptionAsync(CancellationToken ct);

    /// <summary>Asks the application for the result of its health checks.</summary>
    /// <param name="ct">Cancels the question.</param>
    /// <returns>The report, or <see langword="null"/> when the application cannot be reached or does not answer.</returns>
    Task<HealthReport?> GetHealthAsync(CancellationToken ct);
}
