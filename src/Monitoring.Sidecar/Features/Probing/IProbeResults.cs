using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.Probing;

/// <summary>The latest picture of the application, kept up to date by probing it regularly.</summary>
public interface IProbeResults
{
    /// <summary>What is currently known about the application.</summary>
    ApplicationStatus Current { get; }

    /// <summary>Waits until the application has said who it is.</summary>
    /// <param name="ct">Stops the wait.</param>
    /// <returns>The description of the application.</returns>
    Task<InstanceDescription> WaitForDescriptionAsync(CancellationToken ct);
}
