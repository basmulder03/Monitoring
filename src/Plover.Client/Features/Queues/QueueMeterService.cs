using Microsoft.Extensions.Hosting;

namespace Plover.Client.Features.Queues;

/// <summary>Creates the <see cref="QueueMeter"/> when the application starts and disposes it when it stops.</summary>
/// <param name="meter">The meter.</param>
internal sealed class QueueMeterService(QueueMeter meter) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        meter.Dispose();
        return Task.CompletedTask;
    }
}
