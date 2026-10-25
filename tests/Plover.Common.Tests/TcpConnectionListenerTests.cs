using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Plover.Common;
using Shouldly;
using Xunit;

namespace Plover.Common.Tests;

public class TcpConnectionListenerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void LocalEndPoint_is_null_until_started_and_then_the_bound_endpoint()
    {
        // Arrange
        var listener = Listener(Echo);
        var before = listener.LocalEndPoint;

        // Act
        var started = listener.Start();

        // Assert
        before.ShouldBeNull();
        listener.LocalEndPoint.ShouldBe(started);
        started.Port.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Start_rejects_a_second_call()
    {
        // Arrange
        var listener = Listener(Echo);
        listener.Start();

        // Act
        var startAgain = () => listener.Start();

        // Assert
        startAgain.ShouldThrow<InvalidOperationException>();
        await StopAsync(listener);
    }

    [Fact]
    public async Task RunAsync_requires_start_first()
    {
        // Arrange
        var listener = Listener(Echo);

        // Act
        var run = () => listener.RunAsync(Ct);

        // Assert
        await run.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_listener_hands_each_connection_to_the_handler()
    {
        // Arrange
        var listener = Listener(Echo);
        var endpoint = listener.Start();
        using var stop = new CancellationTokenSource();
        var running = listener.RunAsync(stop.Token);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint, Ct);

        // Act
        var answer = await SendAsync(client, "ping");

        // Assert
        answer.ShouldBe("ping");
        await stop.CancelAsync();
        await running;
    }

    [Fact]
    public async Task The_listener_serves_several_connections_at_a_time()
    {
        // Arrange
        var listener = Listener(Echo);
        var endpoint = listener.Start();
        using var stop = new CancellationTokenSource();
        var running = listener.RunAsync(stop.Token);
        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync(endpoint, Ct);
        await second.ConnectAsync(endpoint, Ct);

        // Act
        var fromSecond = await SendAsync(second, "two");
        var fromFirst = await SendAsync(first, "one");

        // Assert
        (fromFirst, fromSecond).ShouldBe(("one", "two"));
        await stop.CancelAsync();
        await running;
    }

    [Fact]
    public async Task A_handler_that_fails_does_not_stop_the_listener()
    {
        // Arrange
        var calls = 0;
        var listener = Listener((stream, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            return Echo(stream, ct);
        });
        var endpoint = listener.Start();
        using var stop = new CancellationTokenSource();
        var running = listener.RunAsync(stop.Token);
        using var broken = new TcpClient();
        await broken.ConnectAsync(endpoint, Ct);
        await Task.Delay(100, Ct);
        using var healthy = new TcpClient();
        await healthy.ConnectAsync(endpoint, Ct);

        // Act
        var answer = await SendAsync(healthy, "still here");

        // Assert
        answer.ShouldBe("still here");
        await stop.CancelAsync();
        await running;
    }

    [Fact]
    public async Task RunAsync_ends_the_connections_and_frees_the_port_when_cancelled()
    {
        // Arrange
        var listener = Listener(WaitForever);
        var endpoint = listener.Start();
        using var stop = new CancellationTokenSource();
        var running = listener.RunAsync(stop.Token);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint, Ct);
        await Task.Delay(100, Ct);

        // Act
        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        // Assert
        var reconnect = async () => await new TcpClient().ConnectAsync(endpoint, Ct);
        await reconnect.ShouldThrowAsync<SocketException>();
    }

    private static TcpConnectionListener Listener(Func<Stream, CancellationToken, Task> handle) =>
        new(IPAddress.Loopback, 0, handle, "tests", NullLogger.Instance);

    private static async Task StopAsync(TcpConnectionListener listener)
    {
        using var stop = new CancellationTokenSource();
        var running = listener.RunAsync(stop.Token);
        await stop.CancelAsync();
        await running;
    }

    /// <summary>Sends the text and returns what the other side sends back.</summary>
    private static async Task<string> SendAsync(TcpClient client, string text)
    {
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), Ct);
        var buffer = new byte[256];
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        var read = await stream.ReadAsync(buffer, guard.Token);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static async Task Echo(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[256];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static async Task WaitForever(Stream stream, CancellationToken ct) => await Task.Delay(Timeout.InfiniteTimeSpan, ct);
}
