using Monitoring.Agent.Features.ControllerLink;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ControllerLink;

public class ControllerLinkHostedServiceTests
{
    [Fact]
    public async Task Service_runs_the_link_while_the_host_runs()
    {
        // Arrange
        var link = new RecordingLink();
        var service = new ControllerLinkHostedService(link);

        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);
        await link.Running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        link.Stopped.ShouldBeFalse();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Service_stops_the_link_when_the_host_stops()
    {
        // Arrange
        var link = new RecordingLink();
        var service = new ControllerLinkHostedService(link);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await link.Running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Act
        await service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        link.Stopped.ShouldBeTrue();
    }

    /// <summary>Link that records how the service drives it.</summary>
    private sealed class RecordingLink : IControllerLink
    {
        private readonly TaskCompletionSource _running = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Running => _running.Task;

        public bool Stopped { get; private set; }

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
