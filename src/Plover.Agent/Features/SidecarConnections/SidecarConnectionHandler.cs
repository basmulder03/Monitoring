using System.Net;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Plover.Agent.Features.Configuration;
using Plover.Agent.Features.MetricsHistory;
using Plover.Agent.Features.ScopeState;
using Plover.Agent.Features.SelfMonitoring;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Protocol.Versioning;

namespace Plover.Agent.Features.SidecarConnections;

/// <summary>
/// Serves the connection of one sidecar: answers its registration, then follows its heartbeats, pulls the health of its application
/// when that may have changed, and tells the scope state what the application reports or that its sidecar is gone.
/// </summary>
/// <remarks>
/// Works on any <see cref="Stream"/> so it can be tested without sockets. Only the handling loop writes to the stream. The handler is shared by
/// all connections; a sidecar that reconnects before its old connection is noticed as dead replaces it, so the old one ending later changes nothing.
/// </remarks>
/// <param name="store">Receives what each application reports.</param>
/// <param name="configuration">The intervals the controller asked for.</param>
/// <param name="options">Timeouts, intervals and limits.</param>
/// <param name="time">Clock for the timeouts and the pull interval.</param>
/// <param name="logger">Receives the reasons a connection was refused or ended.</param>
/// <param name="telemetry">Told about connected sidecars and how long pulls take; optional.</param>
/// <param name="history">Keeps the recent values of each application's metrics; optional.</param>
/// <param name="leadership">When several agents serve one scope, only the leader takes sidecars; the others close the connection, and the sidecar tries again.</param>
internal sealed class SidecarConnectionHandler(
    IScopeStateStore store,
    IAgentConfiguration configuration,
    SidecarConnectionsOptions options,
    TimeProvider time,
    ILogger<SidecarConnectionHandler> logger,
    IAgentTelemetry? telemetry = null,
    IMetricsHistory? history = null,
    ILeadership? leadership = null)
{
    private readonly ConcurrentDictionary<string, Guid> _current = new();

    /// <summary>Serves the connection until the sidecar leaves, goes silent, misbehaves or the agent shuts down.</summary>
    /// <param name="stream">The connection to the sidecar.</param>
    /// <param name="ct">Ends the connection when the agent shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public Task HandleAsync(Stream stream, CancellationToken ct) => HandleAsync(stream, null, ct);

    /// <summary>Serves a connection from a sidecar over TCP, once it has been checked that it comes from the pod it says it is.</summary>
    /// <param name="stream">The connection to the sidecar.</param>
    /// <param name="peer">The address the connection comes from.</param>
    /// <param name="ct">Ends the connection when the agent shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public Task HandlePeerAsync(Stream stream, EndPoint? peer, CancellationToken ct) => HandleAsync(stream, hello => AdmitPeer(hello, peer), ct);

    /// <summary>
    /// Lets a hello through when its sender connects from an address of the pod it names. The platform view holds the addresses of the pods it watches; a pod it does not know,
    /// or knows no address for, cannot be checked and is let through, because what it reports only attaches to instances the platform reports anyway.
    /// </summary>
    private Hello? AdmitPeer(Hello hello, EndPoint? peer)
    {
        if (!options.VerifyPeerAddress || peer is not IPEndPoint remote || store.Find(hello.InstanceId) is not { } pod)
        {
            return hello;
        }

        var known = (pod.Labels.GetValueOrDefault("k8s.pod-ips") ?? pod.Labels.GetValueOrDefault("k8s.pod-ip") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => IPAddress.TryParse(text, out var address) ? address : null)
            .OfType<IPAddress>()
            .ToList();
        var from = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
        if (known.Count == 0 || known.Contains(from))
        {
            return hello;
        }

        logger.LogWarning(
            "Refusing the sidecar of instance '{Instance}': it connects from {From}, which is not an address of that pod. If a proxy or service mesh changes the source address, set Agent:Sidecars:VerifyPeerAddress to false.",
            hello.InstanceId, from);
        return null;
    }

    /// <summary>Serves the connection like <see cref="HandleAsync(Stream, CancellationToken)"/>, but lets the caller decide who the sender is once it has introduced itself.</summary>
    /// <param name="stream">The connection to the sidecar.</param>
    /// <param name="admit">Receives the hello and returns the one to go by, for example with the instance id of the platform's view; <see langword="null"/> refuses the connection.</param>
    /// <param name="ct">Ends the connection when the agent shuts down.</param>
    /// <returns>A task that completes when the connection has ended; it does not throw for connection problems.</returns>
    public async Task HandleAsync(Stream stream, Func<Hello, Hello?>? admit, CancellationToken ct)
    {
        if (leadership is { IsLeader: false })
        {
            logger.LogDebug("Closing a sidecar connection: this agent waits for the lease and does not serve the scope.");
            return;
        }

        var connection = Guid.NewGuid();
        Hello? registered = null;
        var leftOnPurpose = false;
        try
        {
            registered = await ReadHelloAsync(stream, ct).ConfigureAwait(false);
            if (registered is not null && admit is not null)
            {
                var admitted = admit(registered);
                if (admitted is null)
                {
                    logger.LogWarning("Closing connection: the sender of instance '{Instance}' is not one of the services this agent reports", registered.InstanceId);
                    registered = null;
                }
                else
                {
                    registered = admitted;
                }
            }

            if (registered is null)
            {
                return;
            }

            _current[registered.InstanceId] = connection;
            telemetry?.Add("plover.agent.sidecar_connections");
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
            if (registered is not null)
            {
                telemetry?.Add("plover.agent.sidecar_connections", -1);
            }

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
            first = await FrameCodec.ReadAsync(stream, Math.Min(options.MaxFrameBytes, FrameCodec.HandshakeMaxFrameBytes), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Closing sidecar connection: no hello within {Timeout}", options.HelloTimeout);
            return null;
        }

        // A connection that closes without a word is a health probe of the platform (the readiness probe of a highly available agent), not a fault.
        if (first is null)
        {
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
        await PullAsync(stream, PullKind.Health, pulls, ct).ConfigureAwait(false);

        while (true)
        {
            var message = await ReadAsync(stream, ct).ConfigureAwait(false);
            switch (message)
            {
                case null:
                    return false;

                case Heartbeat heartbeat:
                    // One pull per heartbeat at most, health first: it is what the controller reacts to.
                    if (ShouldPullHealth(pulls, heartbeat.Health))
                    {
                        await PullAsync(stream, PullKind.Health, pulls, ct).ConfigureAwait(false);
                    }
                    else if (NextDue(pulls, hello.ServiceName) is { } kind)
                    {
                        await PullAsync(stream, kind, pulls, ct).ConfigureAwait(false);
                    }

                    break;

                case PullResult { Kind: PullKind.Health } result when result.RequestId == pulls.Health.Request:
                    pulls.Health.Outstanding = false;
                    RecordPullDuration(pulls.Health);
                    ApplyHealth(hello, result, pulls);

                    // What the application is built from does not change while it runs, so it is asked once, as soon as it is known to be up.
                    if (!pulls.PackagesAsked && pulls.Report is not null)
                    {
                        pulls.PackagesAsked = true;
                        await PullAsync(stream, PullKind.Packages, pulls, ct).ConfigureAwait(false);
                    }

                    break;

                case PullResult { Kind: PullKind.Packages } result when result.RequestId == pulls.Packages.Request:
                    pulls.Packages.Outstanding = false;
                    ApplyPackages(hello, result, pulls);
                    break;

                case PullResult { Kind: PullKind.Metrics } result when result.RequestId == pulls.Metrics.Request:
                    pulls.Metrics.Outstanding = false;
                    RecordPullDuration(pulls.Metrics);
                    ApplyMetrics(hello, result, pulls);
                    break;

                case PullResult { Kind: PullKind.Edges } result when result.RequestId == pulls.Edges.Request:
                    pulls.Edges.Outstanding = false;
                    ApplyEdges(hello, result, pulls);
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
    private bool ShouldPullHealth(PullState pulls, HealthState heartbeatHealth)
    {
        var due = time.GetUtcNow() - pulls.Health.LastPullAt >= options.HealthPullInterval;
        if (pulls.Health.Outstanding)
        {
            // An answer that never came must not stop the pulls for good.
            return due;
        }

        return due || heartbeatHealth != pulls.Report?.Health;
    }

    /// <summary>
    /// Metrics and edges are pulled on timers, once the application has reported its health, which shows that it is up.
    /// With one pull per heartbeat, of the kinds that are due the one pulled longest ago goes first, so a short interval of one kind cannot starve the other.
    /// </summary>
    private PullKind? NextDue(PullState pulls, string serviceName)
    {
        if (pulls.Report is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var settings = configuration.SettingsFor(serviceName);
        var metricsDue = now - pulls.Metrics.LastPullAt >= TimeSpan.FromSeconds(settings.PullSeconds);
        var edgesDue = now - pulls.Edges.LastPullAt >= TimeSpan.FromSeconds(settings.EdgeFlushSeconds);
        if (metricsDue && edgesDue)
        {
            return pulls.Metrics.LastPullAt <= pulls.Edges.LastPullAt ? PullKind.Metrics : PullKind.Edges;
        }

        return metricsDue ? PullKind.Metrics : edgesDue ? PullKind.Edges : null;
    }

    private void RecordPullDuration(PullProgress pull) =>
        telemetry?.Set("plover.agent.pull_ms", (time.GetUtcNow() - pull.LastPullAt).TotalMilliseconds);

    private async Task PullAsync(Stream stream, PullKind kind, PullState pulls, CancellationToken ct)
    {
        var pull = kind switch { PullKind.Health => pulls.Health, PullKind.Metrics => pulls.Metrics, PullKind.Packages => pulls.Packages, _ => pulls.Edges };
        pull.Request = ++pulls.LastRequestId;
        pull.Outstanding = true;
        pull.LastPullAt = time.GetUtcNow();
        await FrameCodec.WriteAsync(stream, new Pull(pull.Request, kind, 0), options.MaxFrameBytes, ct).ConfigureAwait(false);
    }

    private void ApplyHealth(Hello hello, PullResult result, PullState pulls)
    {
        var health = ReportPayload.ReadHealth(result.PayloadJson);
        if (health is null)
        {
            logger.LogWarning("Ignoring an unreadable health report from instance '{Instance}'", hello.InstanceId);
            return;
        }

        pulls.Report = new ApplicationReport(hello.ServiceName, hello.Version, health.Health, health.Checks, pulls.Report?.Metrics, pulls.Report?.Edges, pulls.Report?.Packages);
        Publish(hello, pulls);
    }

    private void ApplyMetrics(Hello hello, PullResult result, PullState pulls)
    {
        var metrics = ReportPayload.ReadMetrics(result.PayloadJson);
        if (metrics is null || pulls.Report is null)
        {
            // An application that has no metrics, or sent rubbish, is not worth a warning on every pull.
            return;
        }

        pulls.Report = pulls.Report with { Metrics = metrics.Series };
        history?.Record(hello.InstanceId, metrics.Series);
        Publish(hello, pulls);
    }

    // shortcut: the list travels with every instance, so a service with many replicas repeats it; send it once per distinct list when snapshots get large.
    private void ApplyPackages(Hello hello, PullResult result, PullState pulls)
    {
        var packages = ReportPayload.ReadPackages(result.PayloadJson);
        if (packages is null || pulls.Report is null)
        {
            return;
        }

        pulls.Report = pulls.Report with { Packages = packages.Packages };
        Publish(hello, pulls);
    }

    private void ApplyEdges(Hello hello, PullResult result, PullState pulls)
    {
        var edges = ReportPayload.ReadEdges(result.PayloadJson);
        if (edges is null || pulls.Report is null)
        {
            return;
        }

        pulls.Report = pulls.Report with { Edges = edges.Edges };
        Publish(hello, pulls);
    }

    private void Publish(Hello hello, PullState pulls)
    {
        store.Report(hello.InstanceId, pulls.Report!);
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

        history?.Forget(instanceId);

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

        public PullProgress Health { get; } = new();

        public PullProgress Metrics { get; } = new();

        public PullProgress Edges { get; } = new();

        public PullProgress Packages { get; } = new();

        /// <summary>Whether the packages were asked for on this connection.</summary>
        public bool PackagesAsked { get; set; }

        /// <summary>What was last reported to the scope state; <see langword="null"/> before the first health answer.</summary>
        public ApplicationReport? Report { get; set; }
    }

    /// <summary>Where one kind of pull stands.</summary>
    private sealed class PullProgress
    {
        public long Request { get; set; }

        public bool Outstanding { get; set; }

        public DateTimeOffset LastPullAt { get; set; }
    }
}
