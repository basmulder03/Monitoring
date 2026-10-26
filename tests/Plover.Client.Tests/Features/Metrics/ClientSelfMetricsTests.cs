using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Client.Features.Metrics;
using Plover.Protocol.Framing;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Features.Metrics;

/// <summary>What the library says about itself, read from the metrics of a running client.</summary>
[Collection("Meters")]
public class ClientSelfMetricsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IHost> StartAsync(string source, int spanBuffer = 4, string? pipe = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPlover(options =>
        {
            options.SocketPath = Path.Combine(Path.GetTempPath(), $"mon-{Guid.NewGuid():N}.sock");
            options.AgentPipe = pipe;
            options.TraceSources = [source];
            options.SpanBufferSize = spanBuffer;
        });
        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    private static Dictionary<string, double> Metrics(IHost host) =>
        host.Services.GetRequiredService<MetricsCollector>().Read().Series
            .Where(series => series.Name.StartsWith("plover.client.", StringComparison.Ordinal))
            .ToDictionary(series => series.Name, series => series.Value);

    [Fact]
    public async Task The_library_says_how_many_spans_it_keeps_and_how_many_it_had_to_throw_away()
    {
        // Arrange
        var name = $"app-{Guid.NewGuid():N}";
        using var source = new ActivitySource(name);
        using var host = await StartAsync(name, spanBuffer: 4);

        // Act
        for (var i = 0; i < 10; i++)
        {
            using var span = source.StartActivity("work", ActivityKind.Client);
            span!.SetTag("server.address", "payments");
            span.SetTag("http.request.method", "GET");
        }

        // Assert
        // The first read records the series; the count of series is observed before that, so only the second read says how many there are.
        _ = Metrics(host);
        var metrics = Metrics(host);
        metrics["plover.client.spans_buffered"].ShouldBe(4);
        metrics["plover.client.spans_dropped"].ShouldBe(6);
        metrics["plover.client.edges"].ShouldBe(1);
        metrics["plover.client.metric_series"].ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_library_says_whether_a_sidecar_is_connected_to_it()
    {
        // Arrange
        using var host = await StartAsync($"app-{Guid.NewGuid():N}");
        var path = host.Services.GetRequiredService<Plover.Client.Features.LocalServer.PloverOptions>().SocketPath;
        await Eventually(() => File.Exists(path));
        var before = Metrics(host)["plover.client.sidecar_connections"];

        // Act
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), Ct);
        await FrameCodec.WriteAsync(new NetworkStream(socket), new Pull(1, PullKind.Info, 0), ct: Ct);
        using var stream = new NetworkStream(socket);
        await FrameCodec.ReadAsync(stream, ct: Ct);
        var during = Metrics(host)["plover.client.sidecar_connections"];
        socket.Dispose();
        await Eventually(() => Metrics(host)["plover.client.sidecar_connections"] == 0);

        // Assert
        before.ShouldBe(0);
        during.ShouldBe(1);
    }

    [Fact]
    public async Task The_library_says_whether_the_agent_of_the_machine_has_accepted_it()
    {
        // Arrange
        var pipe = $"mon-self-{Guid.NewGuid():N}";
        using var host = await StartAsync($"app-{Guid.NewGuid():N}", pipe: pipe);
        await using var agent = new NamedPipeServerStream(pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await agent.WaitForConnectionAsync(Ct);
        (await FrameCodec.ReadAsync(agent, ct: Ct)).ShouldBeOfType<Hello>();

        // Act
        await FrameCodec.WriteAsync(agent, new Config("v1", [], HeartbeatSeconds: 3600), ct: Ct);
        await Eventually(() => Metrics(host)["plover.client.agent_connected"] == 1);
        agent.Disconnect();
        await Eventually(() => Metrics(host)["plover.client.agent_connected"] == 0);

        // Assert
        Metrics(host)["plover.client.agent_connected"].ShouldBe(0);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, guard.Token);
        }
    }
}
