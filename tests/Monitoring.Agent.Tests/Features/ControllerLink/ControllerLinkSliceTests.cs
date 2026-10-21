using Microsoft.Extensions.DependencyInjection;
using Monitoring.Agent.Features.ControllerLink;
using Monitoring.Agent.Features.ScopeState;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ControllerLink;

public class ControllerLinkSliceTests
{
    private static void Valid(ControllerLinkOptions options)
    {
        options.ControllerAddresses = ["controller:9440"];
        options.Token = "token";
    }

    [Fact]
    public void AddControllerLink_makes_the_link_resolvable_together_with_the_scope_state()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddScopeState(o => o.ScopeName = "shop")
            .AddControllerLink(Valid)
            .BuildServiceProvider();

        // Act
        var link = provider.GetService<IControllerLink>();

        // Assert
        link.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData("tcp://controller:9440")]
    public void AddControllerLink_rejects_an_address_that_is_not_host_and_port(string address)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddControllerLink(o =>
        {
            Valid(o);
            o.ControllerAddresses = [address];
        });

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void AddControllerLink_requires_at_least_one_address()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddControllerLink(o =>
        {
            Valid(o);
            o.ControllerAddresses = [];
        });

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AddControllerLink_requires_a_token(string? token)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddControllerLink(o =>
        {
            Valid(o);
            o.Token = token;
        });

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void AddControllerLink_rejects_a_maximum_delay_below_the_minimum()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddControllerLink(o =>
        {
            Valid(o);
            o.ReconnectMinDelay = TimeSpan.FromSeconds(5);
            o.ReconnectMaxDelay = TimeSpan.FromSeconds(1);
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(nameof(ControllerLinkOptions.ReconnectMinDelay))]
    [InlineData(nameof(ControllerLinkOptions.ConfigReplyTimeout))]
    [InlineData(nameof(ControllerLinkOptions.SnapshotInterval))]
    public void AddControllerLink_rejects_a_duration_that_is_not_positive(string option)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddControllerLink(o =>
        {
            Valid(o);
            var zero = TimeSpan.Zero;
            switch (option)
            {
                case nameof(ControllerLinkOptions.ReconnectMinDelay): o.ReconnectMinDelay = zero; break;
                case nameof(ControllerLinkOptions.ConfigReplyTimeout): o.ConfigReplyTimeout = zero; break;
                default: o.SnapshotInterval = zero; break;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
