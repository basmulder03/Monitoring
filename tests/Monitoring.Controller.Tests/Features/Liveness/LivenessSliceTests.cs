using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Liveness;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Liveness;

public class LivenessSliceTests
{
    [Fact]
    public void AddLiveness_makes_the_tracker_resolvable_as_a_singleton()
    {
        // Arrange
        using var provider = new ServiceCollection().AddLiveness().BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IAgentLiveness>();
        var second = provider.GetRequiredService<IAgentLiveness>();

        // Assert
        first.ShouldBeSameAs(second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLiveness_rejects_a_heartbeat_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddLiveness(o => o.HeartbeatInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AddLiveness_rejects_fewer_than_one_missed_heartbeat(int missed)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddLiveness(o => o.MissedHeartbeats = missed);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
