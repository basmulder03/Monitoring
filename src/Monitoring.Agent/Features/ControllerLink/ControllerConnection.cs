using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>
/// Keeps one controller replica connected: connects, runs a session, and reconnects with backoff and jitter
/// until cancelled. Nothing is ever lost, because every new session starts from a fresh snapshot.
/// </summary>
/// <param name="connect">Opens the connection.</param>
/// <param name="runSession">Runs a session over it.</param>
/// <param name="options">The reconnect delays.</param>
/// <param name="time">Clock for the waits.</param>
/// <param name="random">Returns a random number from 0 up to 1 for the jitter.</param>
/// <param name="logger">Receives connection failures.</param>
internal sealed class ControllerConnection(
    ConnectAsync connect,
    RunSessionAsync runSession,
    ControllerLinkOptions options,
    TimeProvider time,
    Func<double> random,
    ILogger<ControllerConnection> logger)
{
    /// <summary>Connects to the replica and keeps the connection up until cancelled.</summary>
    /// <param name="address">The replica as <c>host:port</c>.</param>
    /// <param name="onConfig">Called with every <see cref="Config"/> the replica sends.</param>
    /// <param name="ct">Stops the loop.</param>
    /// <returns>A task that completes when cancelled or when the agent has said goodbye.</returns>
    public async Task RunAsync(string address, Action<Config> onConfig, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                SessionResult result;
                var stream = await connect(address, ct).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    result = await runSession(stream, onConfig, ct).ConfigureAwait(false);
                }

                if (result.End == SessionEnd.Stopped)
                {
                    return;
                }

                // A session the replica accepted counts as a success even though it ended, and a resync request
                // is not a failure either, so both are retried after the minimum wait. The wait also keeps a
                // replica that keeps asking for resyncs from making the agent reconnect in a tight loop.
                // A refused session is a failure and slows down.
                if (result.Established || result.End == SessionEnd.ResyncRequested)
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                logger.LogDebug("Cannot reach controller replica {Address}: {Reason}", address, ex.Message);
                wait = NextWait(failures);
                failures++;
            }

            try
            {
                await Task.Delay(wait, time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private TimeSpan NextWait(int failureCount) =>
        Backoff.Delay(failureCount, options.ReconnectMinDelay, options.ReconnectMaxDelay, random());
}
