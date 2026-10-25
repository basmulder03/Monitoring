using Plover.Protocol.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Agent.Features.ControllerLink;
using Plover.Agent.Features.SelfMonitoring;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.ControllerLink;

public class ControllerConnectionTests
{
    /// <summary>Delay in seconds before retry number n (0-based) at full jitter, with a 1 s minimum and a 10 s maximum.</summary>
    private static readonly int[] DelaySeconds = [1, 2, 4, 8, 10, 10];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RunAsync_does_not_retry_a_failed_connect_before_the_backoff_has_passed(int failures)
    {
        // Arrange
        var fixture = new ConnectionFixture().WithConnectFailing();
        fixture.Start();
        await fixture.PassFailuresAsync(failures);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(DelaySeconds[failures - 1]) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.ConnectAttempts.ShouldBe(failures);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RunAsync_retries_a_failed_connect_once_the_backoff_has_passed(int failures)
    {
        // Arrange
        var fixture = new ConnectionFixture().WithConnectFailing();
        fixture.Start();
        await fixture.PassFailuresAsync(failures);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(DelaySeconds[failures - 1]));
        await fixture.WaitForAttemptAsync(failures + 1);

        // Assert
        fixture.ConnectAttempts.ShouldBe(failures + 1);
    }

    [Fact]
    public async Task RunAsync_shows_a_replica_that_cannot_be_reached_as_not_connected_and_forgets_it_when_it_is_taken_out()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithConnectFailing();
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);

        // Act
        var whileFailing = fixture.Telemetry.Read().GetValueOrDefault((ReplicaMetrics.Connected, "controller:9440"), -1);
        fixture.Cancel();
        await fixture.Completion;

        // Assert
        whileFailing.ShouldBe(0);
        fixture.Telemetry.Read().Keys.ShouldNotContain(key => key.Replica == "controller:9440");
    }

    [Fact]
    public async Task RunAsync_waits_the_minimum_before_reconnecting_when_the_replica_asks_for_a_new_snapshot()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.ResyncRequested, established: true);
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAttemptAsync(2);

        // Assert
        fixture.ConnectAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_does_not_reconnect_in_a_tight_loop_when_the_replica_keeps_asking_for_a_resync()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.ResyncRequested, established: true);

        // Act
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert: no time passed, so no further attempt
        fixture.ConnectAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_waits_the_minimum_before_reconnecting_after_an_accepted_session_ended()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.Disconnected, established: true);
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAttemptAsync(2);

        // Assert
        fixture.ConnectAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_backs_off_further_after_a_refused_session()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.Refused, established: false);
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAttemptAsync(2);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act: the second wait is 2 s, so 1 s is not enough
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.ConnectAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_starts_over_with_the_minimum_after_a_session_was_accepted()
    {
        // Arrange: two refused sessions (waits of 1 s and 2 s), then an accepted one
        var fixture = new ConnectionFixture().WithSessionResults(
            new SessionResult(SessionEnd.Refused, false),
            new SessionResult(SessionEnd.Refused, false),
            new SessionResult(SessionEnd.Disconnected, true));
        fixture.Start();
        await fixture.WaitForAttemptAsync(1);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAttemptAsync(2);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.WaitForAttemptAsync(3);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act: back to the 1 s minimum, not 4 s
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForAttemptAsync(4);

        // Assert
        fixture.ConnectAttempts.ShouldBe(4);
    }

    [Fact]
    public async Task RunAsync_ends_when_the_session_stopped_for_shutdown()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.Stopped, established: true);

        // Act
        fixture.Start();

        // Assert
        await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.ConnectAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ends_without_throwing_when_cancelled_while_waiting_to_retry()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithConnectFailing();
        fixture.Start();
        await fixture.PassFailuresAsync(1);

        // Act
        fixture.Cancel();

        // Assert
        await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_closes_the_connection_when_the_session_ends()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.Stopped, established: true);

        // Act
        fixture.Start();
        await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        fixture.Streams.Single().Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_hands_the_session_the_callback_for_configuration_changes()
    {
        // Arrange
        var fixture = new ConnectionFixture().WithSessions(SessionEnd.Stopped, established: true).WithSessionSendingConfig(new Config("v9", []));

        // Act
        fixture.Start();
        await fixture.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        fixture.ReceivedConfigs.Select(c => c.Version).ShouldBe(["v9"]);
    }

    /// <summary>Builds a connection loop over fake connect and session steps, with a fake clock and fixed jitter (full).</summary>
    private sealed class ConnectionFixture
    {
        private readonly ControllerLinkOptions _options = new()
        {
            ReconnectMinDelay = TimeSpan.FromSeconds(1),
            ReconnectMaxDelay = TimeSpan.FromSeconds(10),
        };

        private readonly CancellationTokenSource _cancel = new();
        private readonly Queue<SessionResult> _results = new();
        private SessionResult? _repeatResult;
        private bool _connectFails;
        private Config? _configToSend;
        private int _connectAttempts;

        public FakeTimeProvider Clock { get; } = new();

        public List<TrackedStream> Streams { get; } = [];

        public List<Config> ReceivedConfigs { get; } = [];

        public Task Completion { get; private set; } = Task.CompletedTask;

        public int ConnectAttempts => Volatile.Read(ref _connectAttempts);

        public ConnectionFixture WithConnectFailing()
        {
            _connectFails = true;
            return this;
        }

        /// <summary>Every session ends the same way.</summary>
        public ConnectionFixture WithSessions(SessionEnd end, bool established)
        {
            _repeatResult = new SessionResult(end, established);
            return this;
        }

        /// <summary>The sessions end in the given order; the last result repeats.</summary>
        public ConnectionFixture WithSessionResults(params SessionResult[] results)
        {
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }

            _repeatResult = results[^1];
            return this;
        }

        public ConnectionFixture WithSessionSendingConfig(Config config)
        {
            _configToSend = config;
            return this;
        }

        public void Start()
        {
            var connection = new ControllerConnection(ConnectAsync, RunSessionAsync, _options, Clock, () => 1.0, NullLogger<ControllerConnection>.Instance, Telemetry);
            Completion = connection.RunAsync("controller:9440", ReceivedConfigs.Add, _cancel.Token);
        }

        public AgentTelemetry Telemetry { get; } = new();

        public void Cancel() => _cancel.Cancel();

        /// <summary>Gets through the given number of failed attempts, leaving the loop waiting to retry after the last one.</summary>
        public async Task PassFailuresAsync(int failures)
        {
            for (var i = 1; i <= failures; i++)
            {
                await WaitForAttemptAsync(i);
                await Task.Delay(50, TestContext.Current.CancellationToken);
                if (i < failures)
                {
                    Clock.Advance(TimeSpan.FromSeconds(DelaySeconds[i - 1]));
                }
            }
        }

        public async Task WaitForAttemptAsync(int attempt)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (ConnectAttempts < attempt)
            {
                await Task.Delay(5, guard.Token);
            }
        }

        private ValueTask<Stream> ConnectAsync(string address, CancellationToken ct)
        {
            Interlocked.Increment(ref _connectAttempts);
            if (_connectFails)
            {
                throw new IOException("unreachable");
            }

            var stream = new TrackedStream();
            Streams.Add(stream);
            return ValueTask.FromResult<Stream>(stream);
        }

        private Task<SessionResult> RunSessionAsync(string replica, Stream stream, Action<Config> onConfig, CancellationToken ct)
        {
            if (_configToSend is not null)
            {
                onConfig(_configToSend);
            }

            return Task.FromResult(_results.TryDequeue(out var next) ? next : _repeatResult!);
        }
    }

    /// <summary>A stream that remembers whether it was disposed.</summary>
    private sealed class TrackedStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
