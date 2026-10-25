using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Registry;

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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddRegistry_rejects_a_purge_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddRegistry(o => o.PurgeInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddRegistry_registers_the_purge_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "RegistryPurgeService");
    }
}
