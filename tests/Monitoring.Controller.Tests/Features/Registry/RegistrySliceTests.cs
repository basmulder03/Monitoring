using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Registry;

public class RegistrySliceTests
{
    [Fact]
    public void AddRegistry_makes_the_registry_resolvable_as_a_singleton()
    {
        // Arrange
        var services = new ServiceCollection().AddRegistry();
        using var provider = services.BuildServiceProvider();

        // Act
        var first = provider.GetRequiredService<IScopeRegistry>();
        var second = provider.GetRequiredService<IScopeRegistry>();

        // Assert
        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void AddRegistry_rejects_a_negative_retention()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddRegistry(o => o.GoneRetention = TimeSpan.FromSeconds(-1));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
