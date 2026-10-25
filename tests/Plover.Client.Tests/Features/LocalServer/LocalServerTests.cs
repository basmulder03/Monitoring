using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Plover.Client.Features.Health;
using Plover.Protocol.Framing;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Plover.Client.Tests.Features.LocalServer;

/// <summary>The client inside an application, answering over a real Unix socket as the sidecar would ask.</summary>
[Collection("Meters")]
public class LocalServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_application_answers_an_info_pull_with_its_name_and_version()
    {
        // Arrange
        await using var app = await new AppFixture().StartAsync();
        await using var sidecar = await app.ConnectAsync();

        // Act
        var reply = await sidecar.AskAsync(new Pull(1, PullKind.Info, 0));

        // Assert
        var description = ReportPayload.ReadDescription(reply.PayloadJson)!;
        description.ServiceName.ShouldBe("orders");
        description.Version.ShouldBe("1.2.3");
    }

    [Fact]
    public async Task The_application_answers_a_health_pull_with_the_result_of_its_checks()
    {
        // Arrange
        await using var app = await new AppFixture().WithCheck("database", HealthStatus.Unhealthy, "connection refused").StartAsync();
        await using var sidecar = await app.ConnectAsync();

        // Act
        var reply = await sidecar.AskAsync(new Pull(2, PullKind.Health, 0));

        // Assert
        var report = ReportPayload.ReadHealth(reply.PayloadJson)!;
        report.Health.ShouldBe(HealthState.Unhealthy);
        report.Checks.Single().Name.ShouldBe("database");
        report.Checks.Single().Description.ShouldBe("connection refused");
    }

    [Fact]
    public async Task The_application_answers_several_pulls_on_one_connection_in_turn()
    {
        // Arrange
        await using var app = await new AppFixture().StartAsync();
        await using var sidecar = await app.ConnectAsync();

        // Act
        var first = await sidecar.AskAsync(new Pull(10, PullKind.Info, 0));
        var second = await sidecar.AskAsync(new Pull(11, PullKind.Health, 0));
        var third = await sidecar.AskAsync(new Pull(12, PullKind.Info, 0));

        // Assert
        (first.RequestId, second.RequestId, third.RequestId).ShouldBe((10L, 11L, 12L));
    }

    [Fact]
    public async Task The_application_sees_a_check_change_between_two_pulls()
    {
        // Arrange
        var status = HealthStatus.Healthy;
        await using var app = await new AppFixture().WithCheck("database", () => status).StartAsync();
        await using var sidecar = await app.ConnectAsync();
        var before = await sidecar.AskAsync(new Pull(1, PullKind.Health, 0));

        // Act
        status = HealthStatus.Unhealthy;
        var after = await sidecar.AskAsync(new Pull(2, PullKind.Health, 0));

        // Assert
        ReportPayload.ReadHealth(before.PayloadJson)!.Health.ShouldBe(HealthState.Healthy);
        ReportPayload.ReadHealth(after.PayloadJson)!.Health.ShouldBe(HealthState.Unhealthy);
    }

    [Fact]
    public async Task The_application_serves_more_than_one_connection_at_a_time()
    {
        // Arrange
        await using var app = await new AppFixture().StartAsync();
        await using var first = await app.ConnectAsync();
        await using var second = await app.ConnectAsync();

        // Act
        var fromSecond = await second.AskAsync(new Pull(2, PullKind.Info, 0));
        var fromFirst = await first.AskAsync(new Pull(1, PullKind.Info, 0));

        // Assert
        fromFirst.RequestId.ShouldBe(1);
        fromSecond.RequestId.ShouldBe(2);
    }

    [Fact]
    public async Task The_application_ignores_messages_that_are_not_pulls_and_keeps_answering()
    {
        // Arrange
        await using var app = await new AppFixture().StartAsync();
        await using var sidecar = await app.ConnectAsync();
        await sidecar.SendAsync(new Heartbeat(1, HealthState.Healthy));

        // Act
        var reply = await sidecar.AskAsync(new Pull(3, PullKind.Info, 0));

        // Assert
        reply.RequestId.ShouldBe(3);
    }

    [Fact]
    public async Task A_broken_connection_does_not_affect_the_others()
    {
        // Arrange
        await using var app = await new AppFixture().StartAsync();
        await using var broken = await app.ConnectAsync();
        await broken.SendRawAsync([0xFF, 0xFF, 0xFF, 0xFF]);
        await using var healthy = await app.ConnectAsync();

        // Act
        var reply = await healthy.AskAsync(new Pull(4, PullKind.Info, 0));

        // Assert
        reply.RequestId.ShouldBe(4);
    }

    [Fact]
    public async Task A_check_that_throws_does_not_take_the_application_down()
    {
        // Arrange
        await using var app = await new AppFixture().WithThrowingCheck("database").StartAsync();
        await using var sidecar = await app.ConnectAsync();

        // Act
        var health = await sidecar.AskAsync(new Pull(5, PullKind.Health, 0));
        var info = await sidecar.AskAsync(new Pull(6, PullKind.Info, 0));

        // Assert
        ReportPayload.ReadHealth(health.PayloadJson)!.Health.ShouldBe(HealthState.Unhealthy);
        info.RequestId.ShouldBe(6);
    }

    [Fact]
    public async Task The_application_removes_its_socket_when_it_stops()
    {
        // Arrange
        var app = await new AppFixture().StartAsync();
        File.Exists(app.SocketPath).ShouldBeTrue();

        // Act
        await app.DisposeAsync();

        // Assert
        File.Exists(app.SocketPath).ShouldBeFalse();
    }

    [Fact]
    public async Task The_application_replaces_a_socket_file_left_by_an_earlier_run()
    {
        // Arrange
        var path = AppFixture.NewSocketPath();
        await File.WriteAllTextAsync(path, "stale", Ct);

        // Act
        await using var app = await new AppFixture().At(path).StartAsync();
        await using var sidecar = await app.ConnectAsync();
        var reply = await sidecar.AskAsync(new Pull(7, PullKind.Info, 0));

        // Assert
        reply.RequestId.ShouldBe(7);
    }

    [Fact]
    public async Task The_socket_can_be_used_by_other_users_of_the_same_group()
    {
        // Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes do not exist on Windows.");
        await using var app = await new AppFixture().StartAsync();

        // Act
#pragma warning disable CA1416 // skipped on Windows above
        var mode = File.GetUnixFileMode(app.SocketPath);
#pragma warning restore CA1416

        // Assert
        mode.HasFlag(UnixFileMode.GroupRead).ShouldBeTrue();
        mode.HasFlag(UnixFileMode.GroupWrite).ShouldBeTrue();
        mode.HasFlag(UnixFileMode.OtherWrite).ShouldBeFalse();
    }

    [Fact]
    public async Task The_application_does_not_listen_when_plover_is_turned_off()
    {
        // Arrange
        await using var app = await new AppFixture().Disabled().StartAsync();

        // Act
        var listening = File.Exists(app.SocketPath);

        // Assert
        listening.ShouldBeFalse();
    }

    [Fact]
    public async Task The_application_starts_and_runs_when_it_cannot_listen()
    {
        // Arrange: the directory of the socket does not exist, as on a machine without a sidecar
        var app = new AppFixture().At(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "plover.sock"));

        // Act
        var start = async () => await app.StartAsync();

        // Assert
        await start.ShouldNotThrowAsync();
        await app.DisposeAsync();
    }

    /// <summary>Starts an application that uses the client, listening on a socket in the temporary folder.</summary>
    private sealed class AppFixture : IAsyncDisposable
    {
        private readonly List<Action<IHealthChecksBuilder>> _checks = [];
        private bool _enabled = true;
        private IHost? _host;

        public string SocketPath { get; private set; } = NewSocketPath();

        public static string NewSocketPath() => Path.Combine(Path.GetTempPath(), $"mon-{Guid.NewGuid():N}.sock");

        public AppFixture At(string path)
        {
            SocketPath = path;
            return this;
        }

        public AppFixture Disabled()
        {
            _enabled = false;
            return this;
        }

        public AppFixture WithCheck(string name, HealthStatus status, string? description = null)
        {
            _checks.Add(builder => builder.AddCheck(name, () => new HealthCheckResult(status, description)));
            return this;
        }

        public AppFixture WithCheck(string name, Func<HealthStatus> status)
        {
            _checks.Add(builder => builder.AddCheck(name, () => new HealthCheckResult(status())));
            return this;
        }

        public AppFixture WithThrowingCheck(string name)
        {
            _checks.Add(builder => builder.AddCheck(name, new Func<HealthCheckResult>(() => throw new InvalidOperationException("boom"))));
            return this;
        }

        public async Task<AppFixture> StartAsync()
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddLogging();
            builder.Services.AddPlover(options =>
            {
                options.SocketPath = SocketPath;
                options.Enabled = _enabled;
                options.ServiceName = "orders";
                options.Version = "1.2.3";
            });
            var checks = builder.Services.AddHealthChecks();
            foreach (var add in _checks)
            {
                add(checks);
            }

            _host = builder.Build();
            await _host.StartAsync(Ct);

            // Listening starts on a background thread; wait until the socket really accepts connections.
            if (_enabled && Directory.Exists(Path.GetDirectoryName(SocketPath)))
            {
                await WaitUntilListeningAsync();
            }

            return this;
        }

        private async Task WaitUntilListeningAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await probe.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), guard.Token);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(10, guard.Token);
                }
            }
        }

        public async Task<Sidecar> ConnectAsync()
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), Ct);
            return new Sidecar(new NetworkStream(socket, ownsSocket: true));
        }

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.StopAsync(Ct);
                _host.Dispose();
            }
        }
    }

    /// <summary>The sidecar's end of a connection to the application.</summary>
    private sealed class Sidecar(NetworkStream stream) : IAsyncDisposable
    {
        public Task SendAsync(Message message) => FrameCodec.WriteAsync(stream, message, ct: Ct).AsTask();

        public Task SendRawAsync(byte[] bytes) => stream.WriteAsync(bytes, Ct).AsTask();

        /// <summary>Sends a pull and reads the answer. Fails instead of hanging when the application does not answer.</summary>
        public async Task<PullResult> AskAsync(Pull pull)
        {
            await SendAsync(pull);
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(10));
            return (await FrameCodec.ReadAsync(stream, ct: guard.Token)).ShouldBeOfType<PullResult>();
        }

        public async ValueTask DisposeAsync() => await stream.DisposeAsync();
    }
}
