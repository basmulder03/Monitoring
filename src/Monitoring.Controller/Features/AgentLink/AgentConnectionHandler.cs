using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Monitoring.Controller.Features.Configuration;
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
/// <param name="configuration">Decides the rest of the settings each agent is told to use, and says when they change.</param>
/// <param name="applied">Told which configuration each agent says it uses.</param>
/// <param name="options">Timeouts, limits and the configuration handed to agents.</param>
/// <param name="time">Clock for the hello timeout.</param>
/// <param name="logger">Receives the reasons a connection was refused or ended.</param>
/// <param name="connections">Keeps the live connections so requests can reach them; optional.</param>
internal sealed class AgentConnectionHandler(
    IAgentAuthenticator authenticator,
    IScopeRegistry registry,
    IAgentLiveness liveness,
    LivenessOptions livenessOptions,
    IEffectiveConfiguration configuration,
    IAppliedConfigurations applied,
    AgentLinkOptions options,
    TimeProvider time,
    ILogger<AgentConnectionHandler> logger,
    AgentConnections? connections = null)
{
    /// <summary>Serves the connection until the agent leaves, misbehaves or the controller shuts down.</summary>
    /// <param name="stream">The connection to the agent.</param>
    /// <param name="ct">Ends the connection when the controller shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        var connection = Guid.NewGuid();
        AgentIdentity? registered = null;
        AgentConnection? link = null;
        try
        {
            var authenticated = await AuthenticateAsync(stream, ct).ConfigureAwait(false);
            if (authenticated is null)
            {
                return;
            }

            var (identity, hello) = authenticated.Value;

            // Registered before the reply, so the agent never holds its configuration while the controller does not count it as connected.
            registered = identity;
            link = connections?.Add(identity.ScopeName, hello.ProtocolVersion);
            liveness.Connected(identity.ScopeName, connection);
            logger.LogInformation("Agent for scope '{Scope}' connected", identity.ScopeName);

            // Subscribed before the first Config is made, so a change in between is not missed.
            var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
            using var subscription = configuration.OnChanged(() => changes.Writer.TryWrite(true));
            Config ConfigFor() => BuildConfig(configuration.For(identity.ScopeName, hello.Labels));

            await FrameCodec.WriteAsync(stream, ConfigFor(), options.MaxFrameBytes, ct).ConfigureAwait(false);

            await ReceiveAsync(stream, identity, connection, changes.Reader, link, ConfigFor, ct).ConfigureAwait(false);
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
        finally
        {
            // However the connection ended, the scope no longer has this agent.
            if (link is not null)
            {
                connections!.Remove(link);
            }

            if (registered is not null)
            {
                liveness.Disconnected(registered.ScopeName, connection);
                applied.Disconnected(registered.ScopeName, connection);
                logger.LogInformation("Agent for scope '{Scope}' disconnected", registered.ScopeName);
            }
        }
    }

    /// <summary>The <c>Config</c> for an agent: the replicas and the heartbeat of this controller, and the rest from the central configuration.</summary>
    private Config BuildConfig(AgentSettings settings) => new(
        settings.Version,
        options.ControllerReplicas,
        HeartbeatSeconds: Math.Max(1, (int)Math.Ceiling(livenessOptions.HeartbeatInterval.TotalSeconds)),
        PullSeconds: settings.PullSeconds,
        EdgeFlushSeconds: settings.EdgeFlushSeconds);

    /// <summary>Reads and checks the <c>Hello</c>; null when the agent is refused.</summary>
    private async Task<(AgentIdentity Identity, Hello Hello)?> AuthenticateAsync(Stream stream, CancellationToken ct)
    {
        Message? first;
        using var timeout = new CancellationTokenSource(options.HelloTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            first = await FrameCodec.ReadAsync(stream, Math.Min(options.MaxFrameBytes, FrameCodec.HandshakeMaxFrameBytes), linked.Token).ConfigureAwait(false);
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

        return (identity, hello);
    }

    /// <summary>
    /// Applies messages until the agent disconnects, goes silent or sends something it must not, and sends the agent a new
    /// <c>Config</c> whenever the central configuration changes. This loop is the only writer, so writes never overlap.
    /// </summary>
    private async Task ReceiveAsync(
        Stream stream, AgentIdentity identity, Guid connection, ChannelReader<bool> changes, AgentConnection? link, Func<Config> configFor, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<Message?>? reading = null;
        Task<bool>? changed = null;
        Task<bool>? requested = null;
        try
        {
            while (true)
            {
                // A read that is not finished is waited for again, never started twice and never cancelled halfway through a frame.
                reading ??= ReadAsync(stream, stop.Token);
                changed ??= changes.WaitToReadAsync(stop.Token).AsTask();
                requested ??= link?.Outgoing.Reader.WaitToReadAsync(stop.Token).AsTask();
                var ready = await Task.WhenAny(requested is null ? [reading, changed] : [reading, changed, requested]).ConfigureAwait(false);

                if (ready == requested)
                {
                    requested = null;
                    while (link!.Outgoing.Reader.TryRead(out var request))
                    {
                        await FrameCodec.WriteAsync(stream, request, options.MaxFrameBytes, ct).ConfigureAwait(false);
                    }

                    continue;
                }

                if (ready == changed)
                {
                    changed = null;
                    while (changes.TryRead(out _))
                    {
                        // Several changes in a row are one new Config.
                    }

                    await FrameCodec.WriteAsync(stream, configFor(), options.MaxFrameBytes, ct).ConfigureAwait(false);
                    continue;
                }

                var message = await reading.ConfigureAwait(false);
                reading = null;
                if (message is null || !await ApplyAsync(stream, identity, connection, link, message, ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            foreach (var pending in new Task?[] { reading, changed, requested })
            {
                try
                {
                    if (pending is not null)
                    {
                        await pending.ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // The connection is ending; whatever the pending work found does not matter any more.
                }
            }
        }
    }

    /// <summary>Applies one message from the agent; false when the connection must end.</summary>
    private async Task<bool> ApplyAsync(Stream stream, AgentIdentity identity, Guid connection, AgentConnection? link, Message message, CancellationToken ct)
    {
        liveness.Seen(identity.ScopeName, connection);

        switch (message)
        {
            case Snapshot snapshot when snapshot.ScopeName != identity.ScopeName:
                // The check that an agent can never report for another scope.
                logger.LogWarning(
                    "Closing agent connection: scope '{Scope}' may not report as '{Reported}'",
                    identity.ScopeName,
                    snapshot.ScopeName);
                return false;

            case Snapshot snapshot:
                await AcknowledgeAsync(stream, registry.ApplySnapshot(snapshot), ct).ConfigureAwait(false);
                return true;

            case Delta delta:
                await AcknowledgeAsync(stream, registry.ApplyDelta(identity.ScopeName, delta), ct).ConfigureAwait(false);
                return true;

            case Heartbeat heartbeat:
                // Hearing from the agent was recorded above; what it says about its configuration is kept for the drift display.
                if (heartbeat.ConfigVersion is { } version)
                {
                    applied.Report(identity.ScopeName, connection, version);
                }

                return true;

            case HistoryResult result:
                link?.Complete(result);
                return true;

            case Goodbye:
                return false;

            default:
                logger.LogWarning("Closing agent connection: unexpected {Type} message", message.GetType().Name);
                return false;
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
