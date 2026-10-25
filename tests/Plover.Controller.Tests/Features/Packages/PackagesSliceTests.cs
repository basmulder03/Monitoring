using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Packages;

public class PackagesSliceTests
{
    [Fact]
    public void AddPackages_makes_the_inventory_resolvable_together_with_the_registry()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddPackages().BuildServiceProvider();

        // Act
        var inventory = provider.GetService<IPackageInventory>();

        // Assert
        inventory.ShouldNotBeNull();
    }

    [Fact]
    public void AddPackages_rejects_an_alignment_rule_without_a_package()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddPackages(o => o.Aligned.Add(new AlignmentRule(" ", VersionSpread.Exact)));

        // Assert
        add.ShouldThrow<ArgumentException>();
    }
}
