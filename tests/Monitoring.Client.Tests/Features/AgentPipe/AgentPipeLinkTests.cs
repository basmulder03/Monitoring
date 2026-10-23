using System.IO.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Monitoring.Client.Tests.Features.AgentPipe;

/// <summary>The client inside an application, reporting to the agent of the machine over a real named pipe.</summary>
[Collection("Meters")]
public class AgentPipeLinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewPipeName() => $"mon-test-{Guid.NewGuid():N}";

    private static async Task<IHost> StartAppAsync(string? pipe, HealthStatus health = HealthStatus.Healthy, bool enabled = true)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHealthChecks().AddCheck("database", () => new HealthCheckResult(health, "reason"));
        builder.Services.AddMonitoring(options =>
        {
            options.ServiceName = "billing";
            options.Version = "2.0.1";
            options.SocketPath = Path.Combine(Path.GetTempPath(), $"mon-{Guid.NewGuid():N}.sock");
            options.AgentPipe = pipe;
            options.Enabled = enabled;
        });
        var host = builder.Build();
        await host.StartAsync(Ct);
        return host;
    }

    /// <summary>The agent's end: accepts one connection on the pipe.</summary>
    private sealed class FakeAgent(string name) : IAsyncDisposable
    {
        private NamedPipeServerStream? _stream;

        public async Task<Hello> AcceptAsync(Config? config = null)
        {
            _stream = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            // Long, because a cold machine can take a while to start the application, and the test that expects nobody to connect must not end by this alone.
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(60));
            await _stream.WaitForConnectionAsync(guard.Token);
            var hello = (await FrameCodec.ReadAsync(_stream, ct: guard.Token)).ShouldBeOfType<Hello>();
            if (config is not null)
            {
                await SendAsync(config);
            }

            return hello;
        }

        public Task SendAsync(Message message) => FrameCodec.WriteAsync(_stream!, message, ct: Ct).AsTask();

        public async Task<Message?> ReceiveAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(10));
            return await FrameCodec.ReadAsync(_stream!, ct: guard.Token);
        }

        public void Close() => _stream?.Dispose();

        public ValueTask DisposeAsync() => _stream?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_application_introduces_itself_with_its_process_id_name_and_version()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var accepting = agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));

        // Act
        using var app = await StartAppAsync(name);
        var hello = await accepting;

        // Assert
        hello.InstanceId.ShouldBe($"pid-{Environment.ProcessId}");
        hello.ServiceName.ShouldBe("billing");
        hello.Version.ShouldBe("2.0.1");
        hello.Labels["process.id"].ShouldBe(Environment.ProcessId.ToString());
        hello.Labels["process.path"].ShouldNotBeNull();
        hello.Token.ShouldBeNull();
    }

    [Fact]
    public async Task The_application_sends_a_heartbeat_with_its_health_at_the_interval_the_agent_asked_for()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var accepting = agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 1));
        using var app = await StartAppAsync(name, HealthStatus.Degraded);
        await accepting;

        // Act
        var message = await agent.ReceiveAsync();

        // Assert
        var heartbeat = message.ShouldBeOfType<Heartbeat>();
        heartbeat.Health.ShouldBe(HealthState.Degraded);
        heartbeat.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task The_application_answers_the_pulls_of_the_agent()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var accepting = agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));
        using var app = await StartAppAsync(name, HealthStatus.Unhealthy);
        await accepting;

        // Act
        await agent.SendAsync(new Pull(7, PullKind.Health, 0));
        var reply = await agent.ReceiveAsync();

        // Assert
        var result = reply.ShouldBeOfType<PullResult>();
        result.RequestId.ShouldBe(7);
        ReportPayload.ReadHealth(result.PayloadJson)!.Checks.Single().Name.ShouldBe("database");
    }

    [Fact]
    public async Task The_application_says_goodbye_when_it_stops_on_purpose()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var accepting = agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));
        using var app = await StartAppAsync(name);
        await accepting;

        // Act
        var stopping = app.StopAsync(Ct);
        var message = await agent.ReceiveAsync();
        await stopping;

        // Assert
        message.ShouldBeOfType<Goodbye>().InstanceId.ShouldBe($"pid-{Environment.ProcessId}");
    }

    [Fact]
    public async Task The_application_connects_again_after_the_agent_went_away()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var first = agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));
        using var app = await StartAppAsync(name);
        await first;

        // Act
        agent.Close();
        var second = await agent.AcceptAsync(new Config("v1", [], HeartbeatSeconds: 3600));

        // Assert
        second.InstanceId.ShouldBe($"pid-{Environment.ProcessId}");
    }

    [Fact]
    public async Task The_application_carries_on_without_an_agent_and_stops_cleanly()
    {
        // Arrange
        using var app = await StartAppAsync(NewPipeName());

        // Act
        await Task.Delay(300, Ct);
        var stop = () => app.StopAsync(Ct);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task Nothing_is_attempted_without_a_pipe_name_or_with_monitoring_turned_off()
    {
        // Arrange
        var name = NewPipeName();
        await using var agent = new FakeAgent(name);
        var accepting = agent.AcceptAsync();

        // Act
        using var withoutName = await StartAppAsync(pipe: null);
        using var off = await StartAppAsync(name, enabled: false);
        await Task.Delay(500, Ct);

        // Assert
        accepting.IsCompleted.ShouldBeFalse($"{accepting.Status}: {accepting.Exception?.GetBaseException()}");
    }
}
