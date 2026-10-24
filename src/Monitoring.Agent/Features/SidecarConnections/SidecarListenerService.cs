using Microsoft.Extensions.Hosting;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>Hosts the <see cref="ISidecarListener"/>: binds the port when the host starts and serves sidecars until it stops.</summary>
/// <param name="listener">The listener to run.</param>
/// <param name="leadership">When agents compete for a scope, the port is only opened by the leader, so the Service only routes to the leader.</param>
internal sealed class SidecarListenerService(ISidecarListener listener, ILeadership? leadership = null) : BackgroundService
{
    /// <summary>Binds the port, so a port that cannot be used stops the agent from starting instead of failing later.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes once the port is bound.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (leadership is null or { IsLeader: true })
        {
            listener.Start();
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (leadership is not null)
        {
            await leadership.WaitUntilLeaderAsync(stoppingToken).ConfigureAwait(false);
            if (listener.LocalEndPoint is null)
            {
                listener.Start();
            }
        }

        await listener.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
