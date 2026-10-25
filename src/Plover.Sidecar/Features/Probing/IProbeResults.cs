using Plover.Protocol.Reports;

namespace Plover.Sidecar.Features.Probing;

/// <summary>The latest picture of the application, kept up to date by probing it regularly.</summary>
public interface IProbeResults
{
    /// <summary>What is currently known about the application.</summary>
    ApplicationStatus Current { get; }

    /// <summary>Waits until the application has said who it is, which may be immediately.</summary>
    /// <param name="ct">Stops the wait.</param>
    /// <returns>The latest description of the application.</returns>
    Task<InstanceDescription> WaitForDescriptionAsync(CancellationToken ct);
}
