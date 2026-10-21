using Monitoring.Controller.Features.Packages;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Packages;

public class VersionOrderTests
{
    [Theory]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0-beta", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0+abc", "1.0.0+def", 0)]
    [InlineData("10.0.13", "9.0.0", 1)]
    public void Compare_orders_versions_by_their_numbers_and_puts_a_pre_release_before_its_release(string left, string right, int expected)
    {
        // Arrange (the versions come from the test data)

        // Act
        var result = Math.Sign(VersionOrder.Instance.Compare(left, right));

        // Assert
        result.ShouldBe(expected);
    }
}
