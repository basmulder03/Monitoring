using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Health;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Health;

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
