using Microsoft.Extensions.DependencyInjection;
using Monitoring.Sidecar.Features.LibraryLink;
using Shouldly;
using Xunit;

namespace Monitoring.Sidecar.Tests.Features.LibraryLink;

public class LibraryLinkSliceTests
{
    [Fact]
    public void AddLibraryLink_makes_the_link_resolvable()
    {
        // Arrange
        using var provider = new ServiceCollection().AddLibraryLink().BuildServiceProvider();

        // Act
        var link = provider.GetService<ILibraryLink>();

        // Assert
        link.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddLibraryLink_requires_a_socket_path(string path)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddLibraryLink(o => o.SocketPath = path);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddLibraryLink_rejects_a_call_timeout_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddLibraryLink(o => o.CallTimeout = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
