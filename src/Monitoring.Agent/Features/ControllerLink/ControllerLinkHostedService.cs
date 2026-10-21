using Microsoft.Extensions.Hosting;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Hosts the <see cref="IControllerLink"/>: connects to the controller replicas while the host runs.</summary>
/// <param name="link">The link to run.</param>
internal sealed class ControllerLinkHostedService(IControllerLink link) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => link.RunAsync(stoppingToken);
}
