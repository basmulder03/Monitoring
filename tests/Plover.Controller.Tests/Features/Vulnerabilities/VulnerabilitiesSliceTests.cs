using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.Registry;
using Plover.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Vulnerabilities;

public class VulnerabilitiesSliceTests
{
    [Fact]
    public void AddVulnerabilities_makes_the_check_resolvable_together_with_the_inventory()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddPackages().AddVulnerabilities().BuildServiceProvider();

        // Act
        var check = provider.GetService<IVulnerabilities>();

        // Assert
        check.ShouldNotBeNull();
    }

    [Fact]
    public void AddVulnerabilities_registers_the_reload_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddPackages().AddVulnerabilities().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "AdvisoryReloadService");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddVulnerabilities_rejects_a_poll_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddVulnerabilities(o => o.PollInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
