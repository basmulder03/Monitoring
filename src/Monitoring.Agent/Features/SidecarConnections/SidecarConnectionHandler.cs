using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Versioning;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>
/// Serves the connection of one sidecar: answers its registration, then follows its heartbeats, pulls the health of its application
/// when that may have changed, and tells the scope state what the application reports or that its sidecar is gone.
/// </summary>
/// <remarks>
/// Works on any <see cref="Stream"/> so it can be tested without sockets. Only the handling loop writes to the stream. The handler is shared by
/// all connections; a sidecar that reconnects before its old connection is noticed as dead replaces it, so the old one ending later changes nothing.
/// </remarks>
/// <param name="store">Receives what each application reports.</param>
/// <param name="options">Timeouts, intervals and limits.</param>
/// <param name="time">Clock for the timeouts and the pull interval.</param>
/// <param name="logger">Receives the reasons a connection was refused or ended.</param>
internal sealed class SidecarConnectionHandler(
    IScopeStateStore store, SidecarConnectionsOptions options, TimeProvider time, ILogger<SidecarConnectionHandler> logger)
{
    private readonly ConcurrentDictionary<string, Guid> _current = new();

    /// <summary>Serves the connection until the sidecar leaves, goes silent, misbehaves or the agent shuts down.</summary>
    /// <param name="stream">The connection to the sidecar.</param>
    /// <param name="ct">Ends the connection when the agent shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        var connection = Guid.NewGuid();
        Hello? registered = null;
        var leftOnPurpose = false;
        try
        {
            registered = await ReadHelloAsync(stream, ct).ConfigureAwait(false);
            if (registered is null)
            {
                return;
            }

            _current[registered.InstanceId] = connection;
            logger.LogInformation("Sidecar of instance '{Instance}' connected", registered.InstanceId);

            var config = new Config("built-in", [], HeartbeatSeconds: (int)Math.Ceiling(options.HeartbeatInterval.TotalSeconds));
            await FrameCodec.WriteAsync(stream, config, options.MaxFrameBytes, ct).ConfigureAwait(false);

            leftOnPurpose = await FollowAsync(stream, registered, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: the agent is leaving, not the sidecar.
        }
        catch (FrameException ex)
        {
            logger.LogWarning("Closing sidecar connection after a bad frame: {Reason}", ex.Message);
        }
        catch (IOException ex)
        {
            logger.LogDebug("Sidecar connection ended abruptly: {Reason}", ex.Message);
        }
        finally
        {
            // On shutdown the agent is the one going away, and its controllers see that; the sidecars did nothing wrong.
            if (registered is not null && !ct.IsCancellationRequested)
            {
                End(registered.InstanceId, connection, leftOnPurpose);
            }
        }
    }

    /// <summary>Reads and checks the <c>Hello</c>; null when the sidecar is refused.</summary>
    private async Task<Hello?> ReadHelloAsync(Stream stream, CancellationToken ct)
    {
        Message? first;
        using var timeout = new CancellationTokenSource(options.HelloTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            first = await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Closing sidecar connection: no hello within {Timeout}", options.HelloTimeout);
            return null;
        }

        if (first is not Hello hello)
        {
            logger.LogWarning("Closing sidecar connection: the first message was not a hello");
            return null;
        }

        if (!ProtocolVersion.IsCompatible(hello.ProtocolVersion))
        {
            logger.LogWarning("Closing sidecar connection: protocol version {Version} is not supported", hello.ProtocolVersion);
            return null;
        }

        if (string.IsNullOrWhiteSpace(hello.InstanceId) || string.IsNullOrWhiteSpace(hello.ServiceName))
        {
            logger.LogWarning("Closing sidecar connection: the hello does not name the instance and its service");
            return null;
        }

        return hello;
    }

    /// <summary>Follows heartbeats and answers until the connection ends; true when the sidecar said goodbye.</summary>
    private async Task<bool> FollowAsync(Stream stream, Hello hello, CancellationToken ct)
    {
        var pulls = new PullState();
        await PullHealthAsync(stream, pulls, ct).ConfigureAwait(false);

        while (true)
        {
            var message = await ReadAsync(stream, ct).ConfigureAwait(false);
            switch (message)
            {
                case null:
                    return false;

                case Heartbeat heartbeat:
                    if (ShouldPull(pulls, heartbeat.Health))
                    {
                        await PullHealthAsync(stream, pulls, ct).ConfigureAwait(false);
                    }

                    break;

                case PullResult { Kind: PullKind.Health } result when result.RequestId == pulls.LastRequestId:
                    pulls.Outstanding = false;
                    Report(hello, result, pulls);
                    break;

                case Goodbye:
                    return true;

                default:
                    // Results nobody asked for, and anything newer than this agent knows, are not needed.
                    break;
            }
        }
    }

    /// <summary>A pull is needed when the health differs from what was last reported, or the checks have not been read for a while.</summary>
    private bool ShouldPull(PullState pulls, HealthState heartbeatHealth)
    {
        var due = time.GetUtcNow() - pulls.LastPullAt >= options.HealthPullInterval;
        if (pulls.Outstanding)
        {
            // An answer that never came must not stop the pulls for good.
            return due;
        }

        return due || heartbeatHealth != pulls.ReportedHealth;
    }

    private async Task PullHealthAsync(Stream stream, PullState pulls, CancellationToken ct)
    {
        pulls.LastRequestId++;
        pulls.Outstanding = true;
        pulls.LastPullAt = time.GetUtcNow();
        await FrameCodec.WriteAsync(stream, new Pull(pulls.LastRequestId, PullKind.Health, 0), options.MaxFrameBytes, ct).ConfigureAwait(false);
    }

    private void Report(Hello hello, PullResult result, PullState pulls)
    {
        var health = ReportPayload.ReadHealth(result.PayloadJson);
        if (health is null)
        {
            logger.LogWarning("Ignoring an unreadable health report from instance '{Instance}'", hello.InstanceId);
            return;
        }

        pulls.ReportedHealth = health.Health;
        store.Report(hello.InstanceId, new ApplicationReport(hello.ServiceName, hello.Version, health.Health, health.Checks));
        store.Publish();
    }

    /// <summary>Reads the next message; null when the sidecar closed the connection or stayed silent for too long.</summary>
    private async Task<Message?> ReadAsync(Stream stream, CancellationToken ct)
    {
        // A connection can die without being closed. Heartbeats arrive well within this time, so silence means it is gone.
        var silenceLimit = options.HeartbeatInterval * options.MissedHeartbeats;
        using var silence = new CancellationTokenSource(silenceLimit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        try
        {
            return await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Closing sidecar connection: nothing heard for {Timeout}", silenceLimit);
            return null;
        }
    }

    /// <summary>Tells the scope state the sidecar is gone, unless a newer connection of the same instance has taken over.</summary>
    private void End(string instanceId, Guid connection, bool leftOnPurpose)
    {
        if (!_current.TryRemove(new KeyValuePair<string, Guid>(instanceId, connection)))
        {
            return;
        }

        if (leftOnPurpose)
        {
            // A rollout is not a failure: forget the report instead of marking the instance Unhealthy.
            store.ForgetReport(instanceId);
        }
        else
        {
            store.LoseSidecar(instanceId);
        }

        store.Publish();
        logger.LogInformation("Sidecar of instance '{Instance}' disconnected", instanceId);
    }

    private sealed class PullState
    {
        public long LastRequestId { get; set; }

        public bool Outstanding { get; set; }

        public DateTimeOffset LastPullAt { get; set; }

        public HealthState? ReportedHealth { get; set; }
    }
}
