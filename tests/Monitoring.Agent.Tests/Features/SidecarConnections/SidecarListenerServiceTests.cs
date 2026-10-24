using System.Net;
using Monitoring.Agent.Features.SidecarConnections;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.SidecarConnections;

public class SidecarListenerServiceTests
{
    [Fact]
    public async Task StartAsync_binds_the_port_before_it_returns()
    {
        // Arrange
        var listener = new RecordingListener();
        var service = new SidecarListenerService(listener);

        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        listener.Started.ShouldBeTrue();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_fails_when_the_port_cannot_be_bound()
    {
        // Arrange
        var service = new SidecarListenerService(new RecordingListener { FailToStart = true });

        // Act
        var start = async () => await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await start.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Service_serves_sidecars_until_the_host_stops_and_then_ends_the_listener()
    {
        // Arrange
        var listener = new RecordingListener();
        var service = new SidecarListenerService(listener);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await listener.Running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        listener.Stopped.ShouldBeTrue();
    }

    /// <summary>Listener that records how the service drives it.</summary>
    private sealed class RecordingListener : ISidecarListener
    {
        private readonly TaskCompletionSource _running = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailToStart { get; init; }

        public bool Started { get; private set; }

        public IPEndPoint? LocalEndPoint => Started ? new IPEndPoint(IPAddress.Loopback, 1) : null;

        public bool Stopped { get; private set; }

        /// <summary>Completes once <see cref="RunAsync"/> has been entered.</summary>
        public Task Running => _running.Task;

        public IPEndPoint Start()
        {
            if (FailToStart)
            {
                throw new InvalidOperationException("port in use");
            }

            Started = true;
            return new IPEndPoint(IPAddress.Loopback, 1);
        }

        public async Task RunAsync(CancellationToken ct)
        {
            _running.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                Stopped = true;
            }
        }
    }
}
