using Microsoft.Extensions.Logging;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Versioning;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>
/// Serves one agent connection: handshake, then applying the agent's snapshots and deltas to the registry
/// and acknowledging each one.
/// </summary>
/// <remarks>
/// Works on any <see cref="Stream"/> so it can be tested without sockets. Only this class writes to the stream,
/// from a single loop, so no write synchronization is needed yet.
/// </remarks>
/// <param name="authenticator">Decides who the agent is.</param>
/// <param name="registry">Receives the agent's state.</param>
/// <param name="liveness">Told when the agent connects, is heard from and goes away.</param>
/// <param name="livenessOptions">Provides the heartbeat interval agents are told to use and how long silence is tolerated.</param>
/// <param name="options">Timeouts, limits and the configuration handed to agents.</param>
/// <param name="time">Clock for the hello timeout.</param>
/// <param name="logger">Receives the reasons a connection was refused or ended.</param>
internal sealed class AgentConnectionHandler(
    IAgentAuthenticator authenticator,
    IScopeRegistry registry,
    IAgentLiveness liveness,
    LivenessOptions livenessOptions,
    AgentLinkOptions options,
    TimeProvider time,
    ILogger<AgentConnectionHandler> logger)
{
    /// <summary>Serves the connection until the agent leaves, misbehaves or the controller shuts down.</summary>
    /// <param name="stream">The connection to the agent.</param>
    /// <param name="ct">Ends the connection when the controller shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            var identity = await HandshakeAsync(stream, ct).ConfigureAwait(false);
            if (identity is not null)
            {
                var connection = Guid.NewGuid();
                liveness.Connected(identity.ScopeName, connection);
                logger.LogInformation("Agent for scope '{Scope}' connected", identity.ScopeName);
                try
                {
                    await ReceiveAsync(stream, identity, connection, ct).ConfigureAwait(false);
                }
                finally
                {
                    // However the connection ended, the scope no longer has this agent.
                    liveness.Disconnected(identity.ScopeName, connection);
                    logger.LogInformation("Agent for scope '{Scope}' disconnected", identity.ScopeName);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: nothing to report.
        }
        catch (FrameException ex)
        {
            logger.LogWarning("Closing agent connection after a bad frame: {Reason}", ex.Message);
        }
        catch (IOException ex)
        {
            logger.LogDebug("Agent connection ended abruptly: {Reason}", ex.Message);
        }
    }

    /// <summary>Reads and checks the <c>Hello</c> and answers with <c>Config</c>; null when the agent is refused.</summary>
    private async Task<AgentIdentity?> HandshakeAsync(Stream stream, CancellationToken ct)
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
            logger.LogWarning("Closing agent connection: no hello within {Timeout}", options.HelloTimeout);
            return null;
        }

        if (first is not Hello hello)
        {
            logger.LogWarning("Closing agent connection: the first message was not a hello");
            return null;
        }

        if (!ProtocolVersion.IsCompatible(hello.ProtocolVersion))
        {
            logger.LogWarning("Closing agent connection: protocol version {Version} is not supported", hello.ProtocolVersion);
            return null;
        }

        var identity = await authenticator.AuthenticateAsync(hello, ct).ConfigureAwait(false);
        if (identity is null)
        {
            // The token itself is never logged.
            logger.LogWarning("Closing agent connection: authentication failed for instance {InstanceId}", hello.InstanceId);
            return null;
        }

        var config = new Config(
            options.ConfigVersion,
            options.ControllerReplicas,
            HeartbeatSeconds: Math.Max(1, (int)Math.Ceiling(livenessOptions.HeartbeatInterval.TotalSeconds)));
        await FrameCodec.WriteAsync(stream, config, options.MaxFrameBytes, ct).ConfigureAwait(false);
        return identity;
    }

    /// <summary>Applies messages until the agent disconnects, goes silent or sends something it must not.</summary>
    private async Task ReceiveAsync(Stream stream, AgentIdentity identity, Guid connection, CancellationToken ct)
    {
        while (true)
        {
            var message = await ReadAsync(stream, ct).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }

            liveness.Seen(identity.ScopeName, connection);

            switch (message)
            {
                case Snapshot snapshot when snapshot.ScopeName != identity.ScopeName:
                    // The check that an agent can never report for another scope.
                    logger.LogWarning(
                        "Closing agent connection: scope '{Scope}' may not report as '{Reported}'",
                        identity.ScopeName,
                        snapshot.ScopeName);
                    return;

                case Snapshot snapshot:
                    await AcknowledgeAsync(stream, registry.ApplySnapshot(snapshot), ct).ConfigureAwait(false);
                    break;

                case Delta delta:
                    await AcknowledgeAsync(stream, registry.ApplyDelta(identity.ScopeName, delta), ct).ConfigureAwait(false);
                    break;

                case Heartbeat:
                    // Hearing from the agent is all a heartbeat is for; that was recorded above.
                    break;

                case Goodbye:
                    return;

                default:
                    logger.LogWarning("Closing agent connection: unexpected {Type} message", message.GetType().Name);
                    return;
            }
        }
    }

    /// <summary>Reads the next message; null when the agent closed the connection or stayed silent for too long.</summary>
    private async Task<Message?> ReadAsync(Stream stream, CancellationToken ct)
    {
        // A connection can die without being closed. Heartbeats arrive well within this time, so silence means it is gone.
        using var silence = new CancellationTokenSource(livenessOptions.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
        try
        {
            return await FrameCodec.ReadAsync(stream, options.MaxFrameBytes, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Closing agent connection: nothing heard for {Timeout}", livenessOptions.Timeout);
            return null;
        }
    }

    private Task AcknowledgeAsync(Stream stream, ApplyResult result, CancellationToken ct)
    {
        var ack = new Ack(result.AcknowledgedSequence ?? 0, result.Outcome == ApplyOutcome.SnapshotRequired);
        return FrameCodec.WriteAsync(stream, ack, options.MaxFrameBytes, ct).AsTask();
    }
}
