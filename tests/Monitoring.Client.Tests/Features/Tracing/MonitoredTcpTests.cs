using System.Net;
using System.Net.Sockets;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Tracing;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Tracing;

[Collection("Meters")]
public class MonitoredTcpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TraceCollector Collector, EdgeCounters Edges, SpanBuffer Spans)> StartAsync()
    {
        var options = new MonitoringOptions();
        var edges = new EdgeCounters(options.MaxEdges);
        var spans = new SpanBuffer(options.SpanBufferSize);
        var collector = new TraceCollector(options, edges, spans);
        await collector.StartAsync(Ct);
        return (collector, edges, spans);
    }

    [Fact]
    public async Task A_connection_is_counted_as_a_call_to_the_peer_address()
    {
        // Arrange
        var (collector, edges, spans) = await StartAsync();
        using (collector)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Act
            using (await MonitoredTcp.ConnectAsync("127.0.0.1", port, Ct))
            {
            }

            // Assert
            var edge = edges.Read().Edges.Single(e => e.Target == $"127.0.0.1:{port}");
            edge.Type.ShouldBe("TCP");
            edge.Calls.ShouldBe(1);
            edge.Errors.ShouldBe(0);
            spans.ReadAfter(0).Spans.ShouldContain(span => span.Name == "TCP connect");
        }
    }

    [Fact]
    public async Task A_connection_that_fails_is_counted_as_an_error_and_still_throws()
    {
        // Arrange
        var (collector, edges, _) = await StartAsync();
        using (collector)
        {
            var closed = new TcpListener(IPAddress.Loopback, 0);
            closed.Start();
            var port = ((IPEndPoint)closed.LocalEndpoint).Port;
            closed.Stop();

            // Act
            var connect = async () => (await MonitoredTcp.ConnectAsync("127.0.0.1", port, Ct)).Dispose();

            // Assert
            await connect.ShouldThrowAsync<SocketException>();
            var edge = edges.Read().Edges.Single(e => e.Target == $"127.0.0.1:{port}");
            edge.Calls.ShouldBe(1);
            edge.Errors.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Without_monitoring_it_just_connects()
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        // Act
        using var client = await MonitoredTcp.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, Ct);

        // Assert
        client.Connected.ShouldBeTrue();
    }
}
