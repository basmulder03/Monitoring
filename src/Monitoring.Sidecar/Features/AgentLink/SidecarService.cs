using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Common;
using Monitoring.Sidecar.Features.Probing;

namespace Monitoring.Sidecar.Features.AgentLink;

/// <summary>
/// Keeps the application registered with the agent: waits until the application has introduced itself, registers it, and
/// reconnects with backoff and jitter whenever the connection is lost.
/// </summary>
/// <param name="probe">The latest picture of the application.</param>
/// <param name="session">Runs one session over a connection.</param>
/// <param name="connect">Opens the connection to the agent.</param>
/// <param name="options">The agent's address and the reconnect delays.</param>
/// <param name="time">Clock for the waits.</param>
/// <param name="random">Returns a random number from 0 up to 1 for the jitter.</param>
/// <param name="logger">Receives connection failures.</param>
internal sealed class SidecarService(
    IProbeResults probe,
    AgentSession session,
    ConnectAsync connect,
    AgentLinkOptions options,
    TimeProvider time,
    Func<double> random,
    ILogger<SidecarService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Registering before the application has said who it is would give the agent a name that is no use to anyone.
                var description = await probe.WaitForDescriptionAsync(stoppingToken).ConfigureAwait(false);

                TimeSpan wait;
                try
                {
                    var stream = await connect(options.AgentAddress, stoppingToken).ConfigureAwait(false);
                    SessionResult result;
                    await using (stream.ConfigureAwait(false))
                    {
                        result = await session.RunAsync(stream, description, stoppingToken).ConfigureAwait(false);
                    }

                    if (result.End == SessionEnd.Stopped)
                    {
                        return;
                    }

                    if (result.Established)
                    {
                        failures = 0;
                        wait = NextWait(0);
                    }
                    else
                    {
                        wait = NextWait(failures);
                        failures++;
                    }
                }
                catch (Exception ex) when (ex is IOException or SocketException)
                {
                    logger.LogDebug("Cannot reach the agent at {Address}: {Reason}", options.AgentAddress, ex.Message);
                    wait = NextWait(failures);
                    failures++;
                }

                await Task.Delay(wait, time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    private TimeSpan NextWait(int failureCount) =>
        Backoff.Delay(failureCount, options.ReconnectMinDelay, options.ReconnectMaxDelay, random());
}
