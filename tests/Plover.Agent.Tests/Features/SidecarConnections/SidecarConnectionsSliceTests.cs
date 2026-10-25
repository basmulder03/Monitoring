using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Agent.Features.Configuration;
using Plover.Agent.Features.ScopeState;
using Plover.Agent.Features.SidecarConnections;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.SidecarConnections;

public class SidecarConnectionsSliceTests
{
    [Fact]
    public void AddSidecarConnections_makes_the_listener_resolvable_together_with_the_scope_state()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddScopeState(o => o.ScopeName = "shop")
            .AddAgentConfiguration()
            .AddSidecarConnections()
            .BuildServiceProvider();

        // Act
        var listener = provider.GetService<ISidecarListener>();

        // Assert
        listener.ShouldNotBeNull();
    }

    [Fact]
    public void AddSidecarConnections_registers_the_listener_service_as_a_hosted_service()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddScopeState(o => o.ScopeName = "shop")
            .AddAgentConfiguration()
            .AddSidecarConnections()
            .BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>();

        // Assert
        hosted.ShouldContain(service => service.GetType().Name == "SidecarListenerService");
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("")]
    public void AddSidecarConnections_rejects_a_listen_address_that_is_not_an_ip_address(string address)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSidecarConnections(o => o.ListenAddress = address);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void AddSidecarConnections_rejects_a_port_out_of_range(int port)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSidecarConnections(o => o.Port = port);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddSidecarConnections_rejects_a_heartbeat_interval_under_a_second()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSidecarConnections(o => o.HeartbeatInterval = TimeSpan.FromMilliseconds(500));

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddSidecarConnections_rejects_fewer_than_one_missed_heartbeat(int missed)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSidecarConnections(o => o.MissedHeartbeats = missed);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AddSidecarConnections_rejects_a_frame_limit_that_is_not_positive(int bytes)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSidecarConnections(o => o.MaxFrameBytes = bytes);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
