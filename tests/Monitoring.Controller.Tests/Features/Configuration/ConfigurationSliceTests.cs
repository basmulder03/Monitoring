using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class ConfigurationSliceTests
{
    [Fact]
    public void AddConfiguration_makes_the_effective_configuration_resolvable()
    {
        // Arrange
        using var provider = new ServiceCollection().AddConfiguration().BuildServiceProvider();

        // Act
        var configuration = provider.GetService<IEffectiveConfiguration>();

        // Assert
        configuration.ShouldNotBeNull();
    }

    [Fact]
    public void AddConfiguration_registers_the_reload_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddConfiguration().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "ConfigurationReloadService");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddConfiguration_rejects_a_poll_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddConfiguration(o => o.PollInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
