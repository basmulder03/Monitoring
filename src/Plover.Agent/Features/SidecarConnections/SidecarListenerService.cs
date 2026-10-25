using Microsoft.Extensions.Hosting;

namespace Plover.Agent.Features.SidecarConnections;

/// <summary>Hosts the <see cref="ISidecarListener"/>: binds the port when the host starts and serves sidecars until it stops.</summary>
/// <param name="listener">The listener to run.</param>
internal sealed class SidecarListenerService(ISidecarListener listener) : BackgroundService
{
    /// <summary>Binds the port, so a port that cannot be used stops the agent from starting instead of failing later.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes once the port is bound.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => listener.RunAsync(stoppingToken);
}
