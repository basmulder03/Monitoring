using Monitoring.Agent.Features.Configuration;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SelfMonitoring;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Versioning;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>
/// One session with one controller replica: hello, then the scope's snapshot, then its deltas, heartbeats and
/// periodic reconciliation snapshots, while reading the replica's configuration and acknowledgements.
/// </summary>
/// <remarks>
/// Works on any <see cref="Stream"/> so it can be tested without sockets. Only the pump loop writes to the stream,
/// and only the read loop reads from it after the handshake, so neither needs synchronization.
/// </remarks>
/// <param name="store">The scope state this session sends.</param>
/// <param name="configuration">Says which configuration the agent uses, which every heartbeat reports.</param>
/// <param name="options">Identity, token, intervals and limits.</param>
/// <param name="time">Clock for the timeouts and timers.</param>
/// <param name="logger">Receives the reasons a session ended.</param>
/// <param name="telemetry">Told about connections and frames sent; optional.</param>
internal sealed class ControllerSession(
    IScopeStateStore store,
    IAgentConfiguration configuration,
    ControllerLinkOptions options,
    TimeProvider time,
    ILogger<ControllerSession> logger,
    IAgentTelemetry? telemetry = null)
{
    private static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Runs the session until it ends.</summary>
    /// <param name="stream">The connection to the replica.</param>
    /// <param name="onConfig">Called with the replica's configuration, first after the hello and again whenever it changes.</param>
    /// <param name="ct">Ends the session when the agent shuts down.</param>
    /// <returns>How and why the session ended; it does not throw for connection problems.</returns>
    public async Task<SessionResult> RunAsync(Stream stream, Action<Config> onConfig, CancellationToken ct)
    {
        var established = false;
        try
        {
            // The reply timer starts before the hello is sent, so it covers the whole handshake.
            using var replyTimeout = new CancellationTokenSource(options.ConfigReplyTimeout, time);
            await FrameCodec.WriteAsync(stream, CreateHello(), options.MaxFrameBytes, ct).ConfigureAwait(false);

            var config = await ReadConfigAsync(stream, replyTimeout, ct).ConfigureAwait(false);
            if (config is null)
            {
                return new SessionResult(SessionEnd.Refused, Established: false);
            }

            established = true;
            telemetry?.Add("monitoring.agent.controller_connections");
            onConfig(config);

            var end = await SendStateAsync(stream, config, onConfig, ct).ConfigureAwait(false);
            return new SessionResult(end, established);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (established)
            {
                await SayGoodbyeAsync(stream).ConfigureAwait(false);
            }

            return new SessionResult(SessionEnd.Stopped, established);
        }
        catch (FrameException ex)
        {
            logger.LogWarning("Controller session ended after a bad frame: {Reason}", ex.Message);
            return new SessionResult(SessionEnd.Disconnected, established);
        }
        catch (IOException ex)
        {
            logger.LogDebug("Controller session ended abruptly: {Reason}", ex.Message);
            return new SessionResult(SessionEnd.Disconnected, established);
        }
        finally
        {
            if (established)
            {
                telemetry?.Add("monitoring.agent.controller_connections", -1);
            }
        }
    }

    private Hello CreateHello() => new(
        ProtocolVersion.Current,
        options.AgentId,
        "monitoring-agent",
        options.AgentVersion,
        new Dictionary<string, string>(options.Labels),
        options.Token,
        store.ScopeName);

    /// <summary>Waits for the controller's answer to the hello; null when it refuses, stays silent or answers with anything else.</summary>
    private async Task<Config?> ReadConfigAsync(Stream stream, CancellationTokenSource timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var reply = await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, linked.Token).ConfigureAwait(false);
            if (reply is Config config)
            {
                return config;
            }

            logger.LogWarning("Controller refused the agent: {Reply}", reply is null ? "connection closed" : $"unexpected {reply.GetType().Name}");
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Controller did not answer the hello within {Timeout}", options.ConfigReplyTimeout);
            return null;
        }
    }

    /// <summary>Sends the snapshot, then everything that follows, until the session must end.</summary>
    private async Task<SessionEnd> SendStateAsync(Stream stream, Config config, Action<Config> onConfig, CancellationToken ct)
    {
        using var subscription = store.Subscribe();
        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(config.HeartbeatSeconds), time);
        using var snapshotTimer = new PeriodicTimer(options.SnapshotInterval, time);

        // Timers exist before the first write, so nothing sent from here on can outrun them.
        await FrameCodec.WriteAsync(stream, subscription.Initial, options.MaxFrameBytes, ct).ConfigureAwait(false);

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var resyncRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = ReadRepliesAsync(stream, heartbeatTimer, resyncRequested, onConfig, pumpCts.Token);

        Task<bool>? nextDelta = null;
        Task<bool>? nextHeartbeat = null;
        Task<bool>? nextSnapshot = null;
        long heartbeats = 0;

        try
        {
            while (true)
            {
                nextDelta ??= subscription.Deltas.WaitToReadAsync(pumpCts.Token).AsTask();
                nextHeartbeat ??= heartbeatTimer.WaitForNextTickAsync(pumpCts.Token).AsTask();
                nextSnapshot ??= snapshotTimer.WaitForNextTickAsync(pumpCts.Token).AsTask();

                // The order doubles as priority when several are ready: ending conditions first.
                var ready = await Task.WhenAny(reader, resyncRequested.Task, nextDelta, nextHeartbeat, nextSnapshot).ConfigureAwait(false);

                if (ready == reader)
                {
                    await reader.ConfigureAwait(false);
                    return SessionEnd.Disconnected;
                }

                if (ready == resyncRequested.Task)
                {
                    return SessionEnd.ResyncRequested;
                }

                if (ready == nextDelta)
                {
                    var available = nextDelta;
                    nextDelta = null;
                    if (!await SendDeltasAsync(stream, subscription, available, ct).ConfigureAwait(false))
                    {
                        return SessionEnd.ResyncRequested;
                    }
                }
                else if (ready == nextHeartbeat)
                {
                    nextHeartbeat = null;
                    await FrameCodec.WriteAsync(stream, new Heartbeat(++heartbeats, HealthState.Healthy, configuration.Current.Version), options.MaxFrameBytes, ct).ConfigureAwait(false);
                    telemetry?.Add("monitoring.agent.frames_sent");
                }
                else
                {
                    nextSnapshot = null;
                    await FrameCodec.WriteAsync(stream, store.CreateSnapshot(), options.MaxFrameBytes, ct).ConfigureAwait(false);
                    telemetry?.Add("monitoring.agent.frames_sent");
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // The store cut this connection off because it fell too far behind.
            logger.LogWarning("Resyncing with the controller: {Reason}", ex.Message);
            return SessionEnd.ResyncRequested;
        }
        finally
        {
            await pumpCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await reader.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Already reported or irrelevant: the session is ending.
            }
        }
    }

    /// <summary>Sends every delta that is ready; false when the delta stream has ended.</summary>
    private async Task<bool> SendDeltasAsync(Stream stream, StateSubscription subscription, Task<bool> available, CancellationToken ct)
    {
        if (!await available.ConfigureAwait(false))
        {
            return false;
        }

        while (subscription.Deltas.TryRead(out var delta))
        {
            await FrameCodec.WriteAsync(stream, delta, options.MaxFrameBytes, ct).ConfigureAwait(false);
            telemetry?.Add("monitoring.agent.frames_sent");
        }

        return true;
    }

    /// <summary>Reads the replica's messages until the connection ends.</summary>
    private async Task ReadRepliesAsync(
        Stream stream, PeriodicTimer heartbeatTimer, TaskCompletionSource resyncRequested, Action<Config> onConfig, CancellationToken ct)
    {
        while (await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, ct).ConfigureAwait(false) is { } message)
        {
            switch (message)
            {
                case Ack { SnapshotRequired: true }:
                    resyncRequested.TrySetResult();
                    break;

                case Config config:
                    heartbeatTimer.Period = TimeSpan.FromSeconds(config.HeartbeatSeconds);
                    onConfig(config);
                    break;

                default:
                    // Other acknowledgements are not acted on yet; they will feed the replica-lag display.
                    break;
            }
        }
    }

    /// <summary>Tells the replica the agent is leaving on purpose, so it removes the scope's connection quietly.</summary>
    private async Task SayGoodbyeAsync(Stream stream)
    {
        using var grace = new CancellationTokenSource(GoodbyeTimeout);
        try
        {
            await FrameCodec.WriteAsync(stream, new Goodbye(options.AgentId), options.MaxFrameBytes, grace.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or FrameException)
        {
            logger.LogDebug("Could not say goodbye to the controller: {Reason}", ex.Message);
        }
    }
}
