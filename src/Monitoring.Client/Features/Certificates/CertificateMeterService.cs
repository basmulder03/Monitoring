using Microsoft.Extensions.Hosting;

namespace Monitoring.Client.Features.Certificates;

/// <summary>Creates the <see cref="CertificateMeter"/> when the application starts and disposes it when it stops.</summary>
/// <param name="meter">The meter.</param>
internal sealed class CertificateMeterService(CertificateMeter meter) : IHostedService
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
