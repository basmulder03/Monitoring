using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Agent.Features.Configuration;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Configuration;

public class AgentConfigurationTests
{
    private static AgentConfiguration Create() => new(NullLogger<AgentConfiguration>.Instance);

    [Fact]
    public void Current_is_the_built_in_configuration_until_a_controller_has_spoken()
    {
        // Arrange
        var configuration = Create();

        // Act
        var current = configuration.Current;

        // Assert
        current.Version.ShouldBe("built-in");
        current.PullSeconds.ShouldBe(15);
        current.EdgeFlushSeconds.ShouldBe(10);
    }

    [Fact]
    public void Apply_puts_the_configuration_of_the_controller_in_force()
    {
        // Arrange
        var configuration = Create();

        // Act
        configuration.Apply(new Config("v2", [], PullSeconds: 60, EdgeFlushSeconds: 30));

        // Assert
        configuration.Current.Version.ShouldBe("v2");
        configuration.Current.PullSeconds.ShouldBe(60);
        configuration.Current.EdgeFlushSeconds.ShouldBe(30);
    }

    [Fact]
    public void Apply_with_the_same_configuration_again_changes_nothing()
    {
        // Arrange
        var configuration = Create();
        var config = new Config("v2", ["a:1"], PullSeconds: 60, EdgeFlushSeconds: 30);
        configuration.Apply(config);
        var before = configuration.Current;

        // Act
        configuration.Apply(new Config("v2", ["a:1", "b:1"], PullSeconds: 60, EdgeFlushSeconds: 30));

        // Assert
        configuration.Current.ShouldBeSameAs(before);
    }

    [Fact]
    public void AddAgentConfiguration_makes_the_configuration_resolvable()
    {
        // Arrange
        using var provider = new ServiceCollection().AddAgentConfiguration().BuildServiceProvider();

        // Act
        var configuration = provider.GetService<IAgentConfiguration>();

        // Assert
        configuration.ShouldNotBeNull();
    }
}
