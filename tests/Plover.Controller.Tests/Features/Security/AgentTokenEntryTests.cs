using Plover.Controller.Features.Security;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Security;

public class AgentTokenEntryTests
{
    [Theory]
    [InlineData("*", true)]
    [InlineData("shop", false)]
    [InlineData("", false)]
    public void AllowsAnyScope_is_true_only_for_the_enrollment_scope(string scope, bool expected)
    {
        // Arrange
        var entry = new AgentTokenEntry(scope, AgentTokenHash.Compute("t"));

        // Act
        var allowsAny = entry.AllowsAnyScope;

        // Assert
        allowsAny.ShouldBe(expected);
    }
}
