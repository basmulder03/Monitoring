using Plover.Protocol.Health;
using Shouldly;
using Xunit;

namespace Plover.Protocol.Tests.Health;

public class HealthStateExtensionsTests
{
    [Theory]
    [InlineData(HealthState.Healthy, HealthState.Healthy, HealthState.Healthy)]
    [InlineData(HealthState.Healthy, HealthState.Unknown, HealthState.Unknown)]
    [InlineData(HealthState.Unknown, HealthState.Degraded, HealthState.Degraded)]
    [InlineData(HealthState.Degraded, HealthState.Unhealthy, HealthState.Unhealthy)]
    [InlineData(HealthState.Unhealthy, HealthState.Healthy, HealthState.Unhealthy)]
    public void Worst_of_two_returns_the_more_severe_state(HealthState a, HealthState b, HealthState expected)
    {
        // Arrange: states supplied by the test data

        // Act
        var result = a.Worst(b);

        // Assert
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(new[] { HealthState.Healthy, HealthState.Unhealthy, HealthState.Degraded }, HealthState.Unhealthy)]
    [InlineData(new[] { HealthState.Unknown, HealthState.Degraded }, HealthState.Degraded)]
    [InlineData(new[] { HealthState.Healthy, HealthState.Unknown }, HealthState.Unknown)]
    [InlineData(new[] { HealthState.Healthy }, HealthState.Healthy)]
    public void Worst_of_sequence_returns_the_most_severe_state(HealthState[] states, HealthState expected)
    {
        // Arrange: states supplied by the test data

        // Act
        var result = states.Worst();

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void Worst_of_empty_sequence_is_healthy()
    {
        // Arrange
        var states = Array.Empty<HealthState>();

        // Act
        var result = states.Worst();

        // Assert
        result.ShouldBe(HealthState.Healthy);
    }
}
