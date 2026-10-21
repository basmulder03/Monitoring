using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Versioning;
using Monitoring.Sidecar.Features.LibraryLink;
using Monitoring.Sidecar.Features.Probing;

namespace Monitoring.Sidecar.Features.AgentLink;

/// <summary>
/// One session with the agent: it registers the application, then sends a heartbeat with the application's health at the interval
/// the agent asked for and answers the agent's pulls from the latest probe.
/// </summary>
/// <remarks>Works on any <see cref="Stream"/> so it can be tested without sockets. Heartbeats and answers are written under a lock.</remarks>
/// <param name="probe">The latest picture of the application.</param>
/// <param name="link">Asks the application for data the probe does not keep, such as its metrics.</param>
/// <param name="options">Identity, labels and limits.</param>
/// <param name="time">Clock for the timeouts and the heartbeat.</param>
/// <param name="logger">Receives why a session ended.</param>
internal sealed class AgentSession(
    IProbeResults probe, ILibraryLink link, AgentLinkOptions options, TimeProvider time, ILogger<AgentSession> logger)
{
    private static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _writing = new(1, 1);

    /// <summary>Runs the session until it ends.</summary>
    /// <param name="stream">The connection to the agent.</param>
    /// <param name="registeredAs">How the application introduced itself; a different introduction ends the session so it registers again.</param>
    /// <param name="ct">Ends the session when the sidecar shuts down.</param>
    /// <returns>How and why the session ended; it does not throw for connection problems.</returns>
    public async Task<SessionResult> RunAsync(Stream stream, InstanceDescription registeredAs, CancellationToken ct)
    {
        var established = false;
        try
        {
            using var replyTimeout = new CancellationTokenSource(options.ConfigReplyTimeout, time);
            await WriteAsync(stream, CreateHello(registeredAs), ct).ConfigureAwait(false);

            var config = await ReadConfigAsync(stream, replyTimeout, ct).ConfigureAwait(false);
            if (config is null)
            {
                return new SessionResult(SessionEnd.Refused, Established: false);
            }

            established = true;
            var end = await BeatAsync(stream, registeredAs, config, ct).ConfigureAwait(false);
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
            logger.LogWarning("The session with the agent ended after a bad frame: {Reason}", ex.Message);
            return new SessionResult(SessionEnd.Disconnected, established);
        }
        catch (IOException ex)
        {
            logger.LogDebug("The session with the agent ended abruptly: {Reason}", ex.Message);
            return new SessionResult(SessionEnd.Disconnected, established);
        }
    }

    private Hello CreateHello(InstanceDescription description) => new(
        ProtocolVersion.Current,
        options.InstanceId,
        description.ServiceName,
        description.Version,
        new Dictionary<string, string>(options.Labels));

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

            logger.LogWarning("The agent refused the registration: {Reply}", reply is null ? "connection closed" : $"unexpected {reply.GetType().Name}");
            return null;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("The agent did not answer the registration within {Timeout}", options.ConfigReplyTimeout);
            return null;
        }
    }

    /// <summary>Sends a heartbeat on every tick while answering the agent's pulls, until the session must end.</summary>
    private async Task<SessionEnd> BeatAsync(Stream stream, InstanceDescription registeredAs, Config config, CancellationToken ct)
    {
        using var heartbeatTimer = new PeriodicTimer(Interval(config), time);
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadAsync(stream, heartbeatTimer, pumpCts.Token);
        long beats = 0;

        try
        {
            while (true)
            {
                var tick = heartbeatTimer.WaitForNextTickAsync(pumpCts.Token).AsTask();
                var ready = await Task.WhenAny(reader, tick).ConfigureAwait(false);
                if (ready == reader)
                {
                    await reader.ConfigureAwait(false);
                    return SessionEnd.Disconnected;
                }

                await tick.ConfigureAwait(false);
                var status = probe.Current;
                if (status.Description is { } now && now != registeredAs)
                {
                    return SessionEnd.Reregister;
                }

                await WriteAsync(stream, new Heartbeat(++beats, status.Health.Health), ct).ConfigureAwait(false);
            }
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

    /// <summary>Reads the agent's messages: answers pulls from the latest probe and follows changes of the heartbeat interval.</summary>
    private async Task ReadAsync(Stream stream, PeriodicTimer heartbeatTimer, CancellationToken ct)
    {
        while (await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, ct).ConfigureAwait(false) is { } message)
        {
            switch (message)
            {
                case Pull pull:
                    await WriteAsync(stream, await AnswerAsync(pull, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    break;

                case Config config:
                    heartbeatTimer.Period = Interval(config);
                    break;

                default:
                    // Acknowledgements and anything newer are not needed yet.
                    break;
            }
        }
    }

    private async Task<PullResult> AnswerAsync(Pull pull, CancellationToken ct)
    {
        var status = probe.Current;
        var payload = pull.Kind switch
        {
            PullKind.Info when status.Description is { } description => ReportPayload.Write(description),
            PullKind.Health => ReportPayload.Write(status.Health),
            PullKind.Metrics => await link.GetMetricsAsync(ct).ConfigureAwait(false) is { } metrics ? ReportPayload.Write(metrics) : null,
            _ => null,
        };

        return new PullResult(pull.RequestId, pull.Kind, pull.Cursor, payload);
    }

    private static TimeSpan Interval(Config config) => TimeSpan.FromSeconds(Math.Max(1, config.HeartbeatSeconds));

    private async Task WriteAsync(Stream stream, Message message, CancellationToken ct)
    {
        await _writing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(stream, message, options.MaxFrameBytes, ct).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Tells the agent the sidecar is leaving on purpose, so the instance is not marked unhealthy for the missing heartbeats.</summary>
    private async Task SayGoodbyeAsync(Stream stream)
    {
        using var grace = new CancellationTokenSource(GoodbyeTimeout);
        try
        {
            await WriteAsync(stream, new Goodbye(options.InstanceId), grace.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or FrameException)
        {
            logger.LogDebug("Could not say goodbye to the agent: {Reason}", ex.Message);
        }
    }
}
