using Monitoring.Protocol.Reports;
using Monitoring.Sidecar.Features.LibraryLink;

namespace Monitoring.Sidecar.Tests.Shared;

/// <summary>An application that answers metric pulls as the test says, for slices that only ask for them.</summary>
internal sealed class FakeLibraryLink : ILibraryLink
{
    /// <summary>The metrics the application reports; <see langword="null"/> plays an application that cannot be reached.</summary>
    public MetricsReport? Metrics { get; set; }

    /// <inheritdoc />
    public Task<InstanceDescription?> GetDescriptionAsync(CancellationToken ct) => Task.FromResult<InstanceDescription?>(null);

    /// <inheritdoc />
    public Task<HealthReport?> GetHealthAsync(CancellationToken ct) => Task.FromResult<HealthReport?>(null);

    /// <inheritdoc />
    public Task<MetricsReport?> GetMetricsAsync(CancellationToken ct) => Task.FromResult(Metrics);
}
