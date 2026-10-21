using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.LibraryLink;

/// <summary>
/// Default <see cref="ILibraryLink"/>: one connection to the application's socket, opened when needed and opened again
/// after it breaks, so an application that restarts is picked up without any action.
/// </summary>
/// <param name="options">The socket and the time a question may take.</param>
/// <param name="time">Clock for the time a question may take.</param>
/// <param name="logger">Receives why the application could not be reached.</param>
internal sealed class SocketLibraryLink(LibraryLinkOptions options, TimeProvider time, ILogger<SocketLibraryLink> logger) : ILibraryLink, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NetworkStream? _stream;
    private long _nextRequest;
    private bool _wasReachable = true;

    /// <inheritdoc />
    public async Task<InstanceDescription?> GetDescriptionAsync(CancellationToken ct) =>
        ReportPayload.ReadDescription(await AskAsync(PullKind.Info, ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<HealthReport?> GetHealthAsync(CancellationToken ct) =>
        ReportPayload.ReadHealth(await AskAsync(PullKind.Health, ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<MetricsReport?> GetMetricsAsync(CancellationToken ct) =>
        ReportPayload.ReadMetrics(await AskAsync(PullKind.Metrics, ct).ConfigureAwait(false));

    /// <summary>Closes the connection to the application.</summary>
    public void Dispose()
    {
        _stream?.Dispose();
        _gate.Dispose();
    }

    /// <summary>Asks one question; null when the application cannot be reached, does not answer in time or answers without data.</summary>
    private async Task<string?> AskAsync(PullKind kind, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(options.CallTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                var stream = _stream ??= await ConnectAsync(linked.Token).ConfigureAwait(false);
                var request = ++_nextRequest;
                await FrameCodec.WriteAsync(stream, new Pull(request, kind, 0), ct: linked.Token).ConfigureAwait(false);

                // Answers to earlier questions that timed out may still be on the way; skip them.
                while (await FrameCodec.ReadAsync(stream, ct: linked.Token).ConfigureAwait(false) is { } message)
                {
                    if (message is PullResult result && result.RequestId == request)
                    {
                        NoteReachable();
                        return result.PayloadJson;
                    }
                }

                throw new IOException("The application closed the connection.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Drop("did not answer in time");
                return null;
            }
            catch (Exception ex) when (ex is SocketException or IOException or FrameException or UnauthorizedAccessException)
            {
                Drop(ex.Message);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<NetworkStream> ConnectAsync(CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(options.SocketPath), ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private void Drop(string reason)
    {
        _stream?.Dispose();
        _stream = null;

        // Said once per outage, not on every probe.
        if (_wasReachable)
        {
            _wasReachable = false;
            logger.LogWarning("The application is not reachable on {Path}: {Reason}", options.SocketPath, reason);
        }
    }

    private void NoteReachable()
    {
        if (!_wasReachable)
        {
            _wasReachable = true;
            logger.LogInformation("The application is reachable again");
        }
    }
}
