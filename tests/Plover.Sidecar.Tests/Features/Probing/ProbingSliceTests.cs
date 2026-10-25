using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Sidecar.Features.LibraryLink;
using Plover.Sidecar.Features.Probing;
using Shouldly;
using Xunit;

namespace Plover.Sidecar.Tests.Features.Probing;

public class ProbingSliceTests
{
    [Fact]
    public void AddProbing_makes_the_results_resolvable_and_runs_the_probing_in_the_background()
    {
        // Arrange
        using var provider = new ServiceCollection().AddLibraryLink().AddProbing().BuildServiceProvider();

        // Act
        var results = provider.GetService<IProbeResults>();
        var hosted = provider.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        results.ShouldNotBeNull();
        hosted.ShouldBe(["ProbeService"]);
    }

    [Fact]
    public void AddProbing_gives_the_background_service_and_the_results_the_same_instance()
    {
        // Arrange
        using var provider = new ServiceCollection().AddLibraryLink().AddProbing().BuildServiceProvider();

        // Act
        var results = provider.GetRequiredService<IProbeResults>();
        var hosted = provider.GetServices<IHostedService>().Single();

        // Assert
        hosted.ShouldBeSameAs(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddProbing_rejects_an_interval_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddProbing(o => o.Interval = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
