using Plover.Controller.Features.Alerting;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class AlertSilenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("service-down", "shop/orders", "service-down", "shop/orders", true)]
    [InlineData("service-down", "shop/", "service-down", "shop/orders", true)]
    [InlineData("*", "", "restart-loop", "other/x/y", true)]
    [InlineData("service-down", "shop/", "restart-loop", "shop/orders", false)]
    [InlineData("service-down", "shop/", "service-down", "other/orders", false)]
    public void Covers_matches_the_rule_and_the_start_of_the_subject(string rule, string prefix, string alertRule, string subject, bool covered)
    {
        // Arrange
        var silence = new AlertSilence(rule, prefix, Now.AddHours(1));

        // Act
        var result = silence.Covers(alertRule, subject, Now);

        // Assert
        result.ShouldBe(covered);
    }

    [Fact]
    public void Covers_is_false_once_the_silence_has_ended()
    {
        // Arrange
        var silence = new AlertSilence("*", "", Now);

        // Act
        var result = silence.Covers("service-down", "shop/orders", Now);

        // Assert
        result.ShouldBeFalse();
    }
}
