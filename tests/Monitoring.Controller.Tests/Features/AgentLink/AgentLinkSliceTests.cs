using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.AgentLink;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Security;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.AgentLink;

public class AgentLinkSliceTests
{
    [Fact]
    public void AddAgentLink_makes_the_listener_resolvable_together_with_its_dependencies()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddConfiguration().AddSecurity(_ => { }).AddAgentLink().BuildServiceProvider();

        // Act
        var listener = provider.GetService<IAgentListener>();

        // Assert
        listener.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("")]
    public void AddAgentLink_rejects_a_listen_address_that_is_not_an_ip_address(string address)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o => o.ListenAddress = address);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void AddAgentLink_rejects_a_port_out_of_range(int port)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o => o.Port = port);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddAgentLink_rejects_a_hello_timeout_that_is_not_positive(int seconds)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o => o.HelloTimeout = TimeSpan.FromSeconds(seconds));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AddAgentLink_rejects_a_frame_limit_that_is_not_positive(int bytes)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddAgentLink(o => o.MaxFrameBytes = bytes);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddAgentLink_registers_the_listener_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddConfiguration().AddSecurity(_ => { }).AddAgentLink().BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "AgentListenerService");
    }
}
