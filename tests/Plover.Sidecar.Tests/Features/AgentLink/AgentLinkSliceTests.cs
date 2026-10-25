using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Sidecar.Features.AgentLink;
using Plover.Sidecar.Features.LibraryLink;
using Plover.Sidecar.Features.Probing;
using Shouldly;
using Xunit;

namespace Plover.Sidecar.Tests.Features.AgentLink;

public class AgentLinkSliceTests
{
    private static void Valid(AgentLinkOptions options) => options.AgentAddress = "agent:9441";

    [Fact]
    public void AddAgentLink_runs_the_registration_in_the_background()
    {
        // Arrange
        using var provider = new ServiceCollection().AddLibraryLink().AddProbing().AddAgentLink(Valid).BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        hosted.ShouldBe(["ProbeService", "SidecarService"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("agent")]
    [InlineData("tcp://agent:9441")]
    public void AddAgentLink_rejects_an_address_that_is_not_host_and_port(string address)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o => o.AgentAddress = address);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddAgentLink_requires_an_instance_id(string id)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o =>
        {
            Valid(o);
            o.InstanceId = id;
        });

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void AddAgentLink_rejects_a_maximum_delay_below_the_minimum()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o =>
        {
            Valid(o);
            o.ReconnectMinDelay = TimeSpan.FromSeconds(5);
            o.ReconnectMaxDelay = TimeSpan.FromSeconds(1);
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(nameof(AgentLinkOptions.ReconnectMinDelay))]
    [InlineData(nameof(AgentLinkOptions.ConfigReplyTimeout))]
    public void AddAgentLink_rejects_a_duration_that_is_not_positive(string option)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o =>
        {
            Valid(o);
            if (option == nameof(AgentLinkOptions.ReconnectMinDelay))
            {
                o.ReconnectMinDelay = TimeSpan.Zero;
            }
            else
            {
                o.ConfigReplyTimeout = TimeSpan.Zero;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
