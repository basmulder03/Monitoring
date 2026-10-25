using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Controller.Features.Registry;
using Plover.Controller.Features.Topology;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Topology;

public class TopologySliceTests
{
    [Fact]
    public void AddTopology_makes_the_graph_resolvable_together_with_the_registry()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddTopology().BuildServiceProvider();

        // Act
        var topology = provider.GetService<ITopology>();

        // Assert
        topology.ShouldNotBeNull();
    }

    [Fact]
    public void AddTopology_registers_the_sample_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddTopology().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "TopologySampleService");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddTopology_rejects_a_sample_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddTopology(o => o.SampleInterval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddTopology_rejects_a_fade_time_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddTopology(o => o.EdgeFadeAfter = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
