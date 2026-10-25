using System.Diagnostics.Metrics;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Protocol.Framing;
using Plover.Protocol.Messages;

namespace Plover.Client.Features.LocalServer;

/// <summary>
/// Listens on a local socket and answers the sidecar's pulls. It runs inside the application, so it must never get in
/// its way: a problem here is logged and monitoring stops, but the application carries on.
/// </summary>
/// <param name="options">Where to listen, and whether to run at all.</param>
/// <param name="handler">Answers the pulls.</param>
/// <param name="logger">Receives what goes wrong.</param>
internal sealed class LocalServer(PloverOptions options, PullHandler handler, ILogger<LocalServer> logger) : BackgroundService
{
    private int _connections;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Plover is turned off");
            return;
        }

        Socket listener;
        try
        {
            listener = Listen(options.SocketPath);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Typically the application runs without a sidecar, for example on a developer machine.
            logger.LogWarning("Plover is off: cannot listen on {Path}: {Reason}", options.SocketPath, ex.Message);
            return;
        }

        // Whether a sidecar is connected: the state of the socket, for the container of the application.
        using var meter = new Meter("Plover.Client.LocalServer");
        meter.CreateObservableGauge("plover.client.sidecar_connections", () => Volatile.Read(ref _connections), description: "Sidecars connected to the application.");

        using (listener)
        {
            var connections = new List<Task>();
            try
            {
                while (true)
                {
                    var client = await listener.AcceptAsync(stoppingToken).ConfigureAwait(false);
                    connections.RemoveAll(task => task.IsCompleted);
                    connections.Add(ServeAsync(client, stoppingToken));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The application is stopping.
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                logger.LogWarning("Plover stopped listening: {Reason}", ex.Message);
            }
            finally
            {
                TryDelete(options.SocketPath);
                await Task.WhenAll(connections).ConfigureAwait(false);
            }
        }
    }

    private static Socket Listen(string path)
    {
        // A socket file left by a previous run of the application would make binding fail.
        TryDelete(path);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));

            // On OpenShift the application and the sidecar run as arbitrary users of the same group.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            }

            socket.Listen(4);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to clean up, or no right to; either way there is nothing more to do.
        }
    }

    /// <summary>Answers the pulls of one connection until it ends. A fault on one connection never stops the others.</summary>
    private async Task ServeAsync(Socket client, CancellationToken ct)
    {
        using var stream = new NetworkStream(client, ownsSocket: true);
        Interlocked.Increment(ref _connections);
        try
        {
            while (await FrameCodec.ReadAsync(stream, ct: ct).ConfigureAwait(false) is { } message)
            {
                if (message is not Pull pull)
                {
                    logger.LogDebug("Ignoring a {Type} message from the sidecar", message.GetType().Name);
                    continue;
                }

                var result = await handler.HandleAsync(pull, ct).ConfigureAwait(false);
                await FrameCodec.WriteAsync(stream, result, ct: ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The application is stopping.
        }
        catch (Exception ex) when (ex is FrameException or IOException or SocketException)
        {
            logger.LogDebug("A connection of the sidecar ended: {Reason}", ex.Message);
        }
        catch (Exception ex)
        {
            // Whatever a health check does, the application must not be taken down by it.
            logger.LogError(ex, "Answering the sidecar failed");
        }
        finally
        {
            Interlocked.Decrement(ref _connections);
        }
    }
}
