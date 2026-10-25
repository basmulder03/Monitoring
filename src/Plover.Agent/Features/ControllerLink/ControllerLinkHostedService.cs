using Microsoft.Extensions.Hosting;

namespace Plover.Agent.Features.ControllerLink;

/// <summary>Hosts the <see cref="IControllerLink"/>: connects to the controller replicas while the host runs.</summary>
/// <param name="link">The link to run.</param>
/// <param name="leadership">When agents compete for a scope, only the leader connects, so the controllers never see the scope twice.</param>
internal sealed class ControllerLinkHostedService(IControllerLink link, ILeadership? leadership = null) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (leadership is not null)
        {
            await leadership.WaitUntilLeaderAsync(stoppingToken).ConfigureAwait(false);
        }

        await link.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
