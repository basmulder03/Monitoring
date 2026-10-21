using Monitoring.Agent.Features.ControllerLink;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ControllerLink;

public class BackoffTests
{
    private static readonly TimeSpan Min = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 10)]
    [InlineData(40, 10)]
    [InlineData(int.MaxValue, 10)]
    public void Delay_at_full_jitter_doubles_per_failure_up_to_the_maximum(int attempt, int expectedSeconds)
    {
        // Arrange: random = 1 picks the ceiling

        // Act
        var delay = Backoff.Delay(attempt, Min, Max, random: 1);

        // Assert
        delay.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(40)]
    public void Delay_with_no_jitter_is_the_minimum(int attempt)
    {
        // Arrange: random = 0 picks the floor

        // Act
        var delay = Backoff.Delay(attempt, Min, Max, random: 0);

        // Assert
        delay.ShouldBe(Min);
    }

    [Fact]
    public void Delay_picks_a_point_between_the_minimum_and_the_ceiling()
    {
        // Arrange: ceiling of the third retry is 4 s, so halfway between 1 s and 4 s

        // Act
        var delay = Backoff.Delay(2, Min, Max, random: 0.5);

        // Assert
        delay.ShouldBe(TimeSpan.FromSeconds(2.5));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    public void Delay_treats_a_negative_attempt_like_the_first(int attempt)
    {
        // Arrange: nothing to prepare

        // Act
        var delay = Backoff.Delay(attempt, Min, Max, random: 1);

        // Assert
        delay.ShouldBe(Min);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2.0)]
    public void Delay_stays_within_bounds_for_a_random_value_outside_zero_to_one(double random)
    {
        // Arrange: nothing to prepare

        // Act
        var delay = Backoff.Delay(2, Min, Max, random);

        // Assert
        delay.ShouldBeInRange(Min, TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Delay_never_goes_below_the_minimum_when_the_maximum_is_smaller()
    {
        // Arrange
        var smallMax = TimeSpan.FromMilliseconds(100);

        // Act
        var delay = Backoff.Delay(3, Min, smallMax, random: 1);

        // Assert
        delay.ShouldBe(Min);
    }
}
