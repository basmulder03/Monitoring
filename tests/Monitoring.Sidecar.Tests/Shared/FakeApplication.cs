using System.Net.Sockets;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Tests.Shared;

/// <summary>
/// An application with a monitoring client, played by the test: it listens on a Unix socket and answers info and health pulls
/// with whatever the test says. It can go away and come back, and can stay silent or answer late.
/// </summary>
internal sealed class FakeApplication : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Socket? _listener;
    private Task? _serving;

    /// <summary>Where the application listens.</summary>
    public string SocketPath { get; } = Path.Combine(Path.GetTempPath(), $"app-{Guid.NewGuid():N}.sock");

    /// <summary>What the application says about itself.</summary>
    public InstanceDescription Description { get; set; } = new("orders", "1.0.0");

    /// <summary>What the application says about its health.</summary>
    public HealthReport Health { get; set; } = new(HealthState.Healthy, []);

    /// <summary>What the application says about its metrics.</summary>
    public MetricsReport Metrics { get; set; } = new([]);

    /// <summary>When set, the application reads questions but never answers them.</summary>
    public bool Silent { get; set; }

    /// <summary>How long the application waits before answering its first question; later ones are answered at once.</summary>
    public TimeSpan FirstAnswerDelay { get; set; }

    /// <summary>How many questions have been asked.</summary>
    public int QuestionsAsked => Volatile.Read(ref _questions);

    private int _questions;

    /// <summary>Starts listening.</summary>
    /// <returns>This application, for chaining.</returns>
    public FakeApplication Start()
    {
        File.Delete(SocketPath);
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listener.Listen(4);
        _serving = Task.Run(AcceptAsync);
        return this;
    }

    /// <summary>Stops listening and drops the connections, as a crashing application does.</summary>
    /// <returns>A task that completes once the application is gone.</returns>
    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        _listener?.Dispose();
        if (_serving is not null)
        {
            await _serving.WaitAsync(TimeSpan.FromSeconds(5));
        }

        File.Delete(SocketPath);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task AcceptAsync()
    {
        var connections = new List<Task>();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener!.AcceptAsync(_stop.Token);
                connections.Add(ServeAsync(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }

        await Task.WhenAll(connections);
    }

    private async Task ServeAsync(Socket client)
    {
        using var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            while (await FrameCodec.ReadAsync(stream, ct: _stop.Token) is Pull pull)
            {
                var number = Interlocked.Increment(ref _questions);
                if (Silent)
                {
                    continue;
                }

                if (number == 1 && FirstAnswerDelay > TimeSpan.Zero)
                {
                    await Task.Delay(FirstAnswerDelay, _stop.Token);
                }

                var payload = pull.Kind switch
                {
                    PullKind.Info => ReportPayload.Write(Description),
                    PullKind.Health => ReportPayload.Write(Health),
                    PullKind.Metrics => ReportPayload.Write(Metrics),
                    _ => null,
                };
                await FrameCodec.WriteAsync(stream, new PullResult(pull.RequestId, pull.Kind, pull.Cursor, payload), ct: _stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or FrameException)
        {
            // The connection or the application ended.
        }
    }
}
