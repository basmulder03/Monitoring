using Microsoft.Extensions.Hosting;

namespace Plover.Controller.Features.AgentLink;

/// <summary>Hosts the <see cref="IAgentListener"/>: binds the port when the host starts and serves agents until it stops.</summary>
/// <param name="listener">The listener to run.</param>
internal sealed class AgentListenerService(IAgentListener listener) : BackgroundService
{
    /// <summary>Binds the port, so a port that cannot be used stops the controller from starting instead of failing later.</summary>
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
