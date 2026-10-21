using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Health;

public class HealthSliceTests
{
    [Fact]
    public void AddHealth_makes_the_roll_up_resolvable_together_with_the_registry()
    {
        // Arrange
        var services = new ServiceCollection().AddRegistry().AddLiveness().AddHealth();
        using var provider = services.BuildServiceProvider();

        // Act
        var rollup = provider.GetService<IHealthRollup>();

        // Assert
        rollup.ShouldNotBeNull();
    }
}
