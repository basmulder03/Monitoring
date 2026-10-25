using Plover.Protocol.Health;
using Plover.Protocol.Reports;
using Plover.Sidecar.Features.Probing;

namespace Plover.Sidecar.Tests.Shared;

/// <summary>The picture of the application as the test says it is, for slices that only read it.</summary>
internal sealed class FakeProbeResults : IProbeResults
{
    private readonly TaskCompletionSource<InstanceDescription> _introduced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ApplicationStatus _current = new(null, new HealthReport(HealthState.Unhealthy, []), null);

    /// <inheritdoc />
    public ApplicationStatus Current => Volatile.Read(ref _current);

    /// <summary>Sets what is known about the application, introducing it when it has a description.</summary>
    /// <param name="description">Who the application is.</param>
    /// <param name="health">The health report.</param>
    public void Set(InstanceDescription? description, HealthReport health)
    {
        Volatile.Write(ref _current, new ApplicationStatus(description, health, description is null ? null : DateTimeOffset.UnixEpoch));
        if (description is not null)
        {
            _introduced.TrySetResult(description);
        }
    }

    /// <summary>Sets a healthy application with the given name and version.</summary>
    /// <param name="serviceName">Service name.</param>
    /// <param name="version">Version.</param>
    /// <param name="health">Its health, Healthy by default.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeProbeResults Running(string serviceName, string? version = null, HealthState health = HealthState.Healthy)
    {
        Set(new InstanceDescription(serviceName, version), new HealthReport(health, []));
        return this;
    }

    /// <inheritdoc />
    public async Task<InstanceDescription> WaitForDescriptionAsync(CancellationToken ct)
    {
        await _introduced.Task.WaitAsync(ct);
        return Current.Description!;
    }
}
