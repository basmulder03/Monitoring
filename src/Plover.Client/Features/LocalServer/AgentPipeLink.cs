using System.Diagnostics.Metrics;
using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Protocol.Versioning;

namespace Plover.Client.Features.LocalServer;

/// <summary>
/// Connects the application to the Plover agent of the machine over a named pipe, which is how it works on a Windows server where there is no sidecar:
/// it introduces itself, sends a heartbeat with its health and answers the agent's pulls, exactly as a sidecar does for the application next to it.
/// It runs inside the application, so it must never get in its way: a failure is logged quietly and tried again later.
/// </summary>
/// <param name="options">The name of the pipe, and whether to run at all.</param>
/// <param name="handler">Answers the pulls, and so also says what the application is called and how healthy it is.</param>
/// <param name="logger">Receives what goes wrong.</param>
internal sealed class AgentPipeLink(
    PloverOptions options, PullHandler handler, ILogger<AgentPipeLink> logger) : BackgroundService
{
    /// <summary>The label in which the application tells the agent its process id, which is how the agent finds the service it runs in.</summary>
    public const string ProcessIdLabel = "process.id";

    /// <summary>The label in which the application tells the agent the program it runs from.</summary>
    public const string ProcessPathLabel = "process.path";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfigTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _writing = new(1, 1);
    private int _connected;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.AgentPipe))
        {
            return;
        }

        // Whether the agent has this application on its books, for the container of the application.
        using var meter = new Meter("Plover.Client.AgentPipe");
        meter.CreateObservableGauge("plover.client.agent_connected", () => Volatile.Read(ref _connected), description: "1 while the agent of the machine accepted this application.");

        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                failures = await RunSessionAsync(stoppingToken).ConfigureAwait(false) ? 0 : failures + 1;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or FrameException or TimeoutException or UnauthorizedAccessException)
            {
                // No agent on this machine, or one that does not let this account in: the application is not affected either way.
                logger.LogDebug("Cannot reach the Plover agent on '{Pipe}': {Reason}", options.AgentPipe, ex.Message);
                failures++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The link to the Plover agent failed");
                failures++;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(MaxWait.TotalSeconds, 1 << Math.Min(failures, 5))), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Runs one connection until it ends; true when the agent accepted the application.</summary>
    private async Task<bool> RunSessionAsync(CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", options.AgentPipe!, PipeDirection.InOut, PipeOptions.Asynchronous);
        using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connecting.CancelAfter(ConnectTimeout);
            try
            {
                await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("No agent is listening.");
            }
        }

        var instanceId = $"pid-{Environment.ProcessId}";
        var description = ReportPayload.ReadDescription((await handler.HandleAsync(new Pull(0, PullKind.Info, 0), ct).ConfigureAwait(false)).PayloadJson)
            ?? new InstanceDescription("unknown", null);
        await WriteAsync(pipe, new Hello(
            ProtocolVersion.Current,
            instanceId,
            description.ServiceName,
            description.Version,
            new Dictionary<string, string>
            {
                [ProcessIdLabel] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [ProcessPathLabel] = Environment.ProcessPath ?? "",
            }), ct).ConfigureAwait(false);

        Config config;
        using (var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            waiting.CancelAfter(ConfigTimeout);
            try
            {
                if (await FrameCodec.ReadAsync(pipe, FrameCodec.HandshakeMaxFrameBytes, waiting.Token).ConfigureAwait(false) is not Config reply)
                {
                    // The agent closed the connection: it does not report this application, for example because no service of it is listed.
                    return false;
                }

                config = reply;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("The agent did not answer.");
            }
        }

        logger.LogInformation("Reporting to the Plover agent on '{Pipe}'", options.AgentPipe);
        Volatile.Write(ref _connected, 1);
        try
        {
            await BeatAsync(pipe, config, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await SayGoodbyeAsync(pipe, instanceId).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _connected, 0);
        }

        return true;
    }

    /// <summary>Sends a heartbeat on every tick while answering the agent's pulls, until the connection ends.</summary>
    private async Task BeatAsync(NamedPipeClientStream pipe, Config config, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval(config));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadAsync(pipe, timer, stop.Token);
        long beats = 0;
        try
        {
            while (true)
            {
                var tick = timer.WaitForNextTickAsync(stop.Token).AsTask();
                var ready = await Task.WhenAny(reader, tick).ConfigureAwait(false);
                if (ready == reader)
                {
                    await reader.ConfigureAwait(false);
                    return;
                }

                await tick.ConfigureAwait(false);
                var report = ReportPayload.ReadHealth((await handler.HandleAsync(new Pull(0, PullKind.Health, 0), ct).ConfigureAwait(false)).PayloadJson);
                await WriteAsync(pipe, new Heartbeat(++beats, report?.Health ?? HealthState.Unknown), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await reader.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The connection is ending; what the reader found no longer matters.
            }
        }
    }

    /// <summary>Answers the agent's pulls and follows changes of the heartbeat interval until the agent closes the connection.</summary>
    private async Task ReadAsync(NamedPipeClientStream pipe, PeriodicTimer timer, CancellationToken ct)
    {
        while (await FrameCodec.ReadAsync(pipe, ct: ct).ConfigureAwait(false) is { } message)
        {
            switch (message)
            {
                case Pull pull:
                    await WriteAsync(pipe, await handler.HandleAsync(pull, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    break;

                case Config config:
                    timer.Period = Interval(config);
                    break;
            }
        }
    }

    private static TimeSpan Interval(Config config) => TimeSpan.FromSeconds(Math.Max(1, config.HeartbeatSeconds));

    private async Task WriteAsync(Stream stream, Message message, CancellationToken ct)
    {
        await _writing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteAsync(stream, message, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>Tells the agent the application is leaving on purpose, so it is not reported as lost.</summary>
    private async Task SayGoodbyeAsync(Stream stream, string instanceId)
    {
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await WriteAsync(stream, new Goodbye(instanceId), grace.Token).ConfigureAwait(false);

            // Wait for the agent to close the connection. Closing first, with something it sent still unread, makes the system reset the connection,
            // and a reset can throw away the goodbye before the agent has read it, which would show the application as lost instead of gone on purpose.
            while (await FrameCodec.ReadAsync(stream, ct: grace.Token).ConfigureAwait(false) is not null)
            {
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or FrameException)
        {
            logger.LogDebug("Could not say goodbye to the agent: {Reason}", ex.Message);
        }
    }
}
