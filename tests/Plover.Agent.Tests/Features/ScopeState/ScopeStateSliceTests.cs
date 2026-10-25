using Microsoft.Extensions.DependencyInjection;
using Plover.Agent.Features.ScopeState;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.ScopeState;

public class ScopeStateSliceTests
{
    [Fact]
    public void AddScopeState_makes_the_store_resolvable_as_a_singleton_with_the_configured_scope()
    {
        // Arrange
        using var provider = new ServiceCollection().AddScopeState(o => o.ScopeName = "shop").BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IScopeStateStore>();
        var second = provider.GetRequiredService<IScopeStateStore>();

        // Assert
        first.ShouldBeSameAs(second);
        first.ScopeName.ShouldBe("shop");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddScopeState_requires_a_scope_name(string name)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddScopeState(o => o.ScopeName = name);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddScopeState_rejects_a_buffer_that_is_not_positive(int size)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddScopeState(o =>
        {
            o.ScopeName = "shop";
            o.SubscriberBuffer = size;
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
