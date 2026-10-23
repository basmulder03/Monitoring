using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.Configuration;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SidecarConnections;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.SidecarConnections;

/// <summary>The agent listening on a real named pipe for the applications of the machine.</summary>
public class PipeListenerServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeProcesses(params (int Pid, string Instance)[] map) : IWindowsProcessMap
    {
        public string? InstanceOfProcess(int processId) => map.Where(m => m.Pid == processId).Select(m => m.Instance).FirstOrDefault();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PipeListenerService _service;

        public Fixture(IWindowsProcessMap? processes, string? pipe = null)
        {
            Pipe = pipe ?? $"mon-agent-{Guid.NewGuid():N}";
            Store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "server-1" });
            var options = new SidecarConnectionsOptions { PipeName = Pipe, HeartbeatInterval = TimeSpan.FromSeconds(5) };
            var handler = new SidecarConnectionHandler(Store, new AgentConfiguration(NullLogger<AgentConfiguration>.Instance), options, new FakeTimeProvider(), NullLogger<SidecarConnectionHandler>.Instance);
            _service = new PipeListenerService(handler, options, NullLogger<PipeListenerService>.Instance, processes);
        }

        public string Pipe { get; }

        public ScopeStateStore Store { get; }

        public async Task<Fixture> StartAsync()
        {
            await _service.StartAsync(Ct);
            return this;
        }

        public async Task<NamedPipeClientStream> ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, Ct);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            await _service.StopAsync(Ct);
            _service.Dispose();
        }
    }

    private static Hello Hello(int pid, string instance = "pid-1") =>
        new(ProtocolVersion, instance, "billing", "2.0.1", new Dictionary<string, string> { ["process.id"] = pid.ToString() });

    private const int ProtocolVersion = Monitoring.Protocol.Versioning.ProtocolVersion.Current;

    private static async Task<Message?> ReadAsync(Stream stream)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        return await FrameCodec.ReadAsync(stream, ct: guard.Token);
    }

    [Fact]
    public async Task An_application_is_matched_with_the_service_that_runs_in_its_process_and_reports_for_it()
    {
        // Arrange
        await using var fixture = await new Fixture(new FakeProcesses((4242, "Billing"))).StartAsync();
        fixture.Store.Upsert(new InstanceInfo("Billing", "Billing", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));
        await using var app = await fixture.ConnectAsync();

        // Act
        await FrameCodec.WriteAsync(app, Hello(4242), ct: Ct);
        (await ReadAsync(app)).ShouldBeOfType<Config>();
        var healthPull = (await ReadAsync(app)).ShouldBeOfType<Pull>();
        await FrameCodec.WriteAsync(app, new PullResult(healthPull.RequestId, PullKind.Health, 0,
            ReportPayload.Write(new HealthReport(HealthState.Degraded, [new HealthCheckResult("queue", HealthState.Degraded, "backed up")]))), ct: Ct);

        // Assert
        await Eventually(() => fixture.Store.CreateSnapshot().Instances.Single().Health == HealthState.Degraded);
        var instance = fixture.Store.CreateSnapshot().Instances.Single();
        instance.Id.ShouldBe("Billing");
        instance.Version.ShouldBe("2.0.1");
        instance.Checks!.Single().Name.ShouldBe("queue");
    }

    [Fact]
    public async Task An_application_in_a_process_no_listed_service_runs_in_is_turned_away()
    {
        // Arrange
        await using var fixture = await new Fixture(new FakeProcesses((4242, "Billing"))).StartAsync();
        await using var app = await fixture.ConnectAsync();

        // Act
        await FrameCodec.WriteAsync(app, Hello(9999), ct: Ct);
        var reply = await ReadAsync(app);

        // Assert
        reply.ShouldBeNull();
        fixture.Store.CreateSnapshot().Instances.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_application_that_does_not_say_its_process_id_is_turned_away_when_processes_are_matched()
    {
        // Arrange
        await using var fixture = await new Fixture(new FakeProcesses((4242, "Billing"))).StartAsync();
        await using var app = await fixture.ConnectAsync();

        // Act
        await FrameCodec.WriteAsync(app, new Hello(ProtocolVersion, "x", "billing", null, new Dictionary<string, string>()), ct: Ct);

        // Assert
        (await ReadAsync(app)).ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_match_of_processes_an_application_is_taken_at_its_word()
    {
        // Arrange
        await using var fixture = await new Fixture(processes: null).StartAsync();
        await using var app = await fixture.ConnectAsync();

        // Act
        await FrameCodec.WriteAsync(app, Hello(1, "pid-1"), ct: Ct);

        // Assert
        (await ReadAsync(app)).ShouldBeOfType<Config>();
    }

    [Fact]
    public async Task Several_applications_can_be_connected_at_once()
    {
        // Arrange
        await using var fixture = await new Fixture(new FakeProcesses((1, "A"), (2, "B"))).StartAsync();
        await using var first = await fixture.ConnectAsync();
        await using var second = await fixture.ConnectAsync();

        // Act
        await FrameCodec.WriteAsync(first, Hello(1), ct: Ct);
        await FrameCodec.WriteAsync(second, Hello(2), ct: Ct);

        // Assert
        (await ReadAsync(first)).ShouldBeOfType<Config>();
        (await ReadAsync(second)).ShouldBeOfType<Config>();
    }

    [Fact]
    public void On_windows_the_pipe_is_limited_to_the_accounts_of_the_list_and_refuses_an_account_that_does_not_exist()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The access list of a pipe exists only on Windows.");
            return;
        }

        AssertWindowsAccessList();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertWindowsAccessList()
    {
        // Act
        var security = PipeListenerService.SecurityFor(["BUILTIN\\Users"]);
        using var pipe = PipeListenerService.CreateServer($"mon-acl-{Guid.NewGuid():N}", ["BUILTIN\\Users"]);
        var unknown = () => PipeListenerService.SecurityFor(["NO-SUCH-ACCOUNT\\nobody"]);

        // Assert: the system, the administrators, the agent itself, two service accounts and the one in the list
        security.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)).Count.ShouldBe(6);
        unknown.ShouldThrow<ArgumentException>();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, guard.Token);
        }
    }
}
