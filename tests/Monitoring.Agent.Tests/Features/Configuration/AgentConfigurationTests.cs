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

    private static Config Central(string[]? overridable = null) => new("v1", [], PullSeconds: 20, EdgeFlushSeconds: 8, Services: new Dictionary<string, ServiceConfig> { ["orders"] = new(PullSeconds: 60, EdgeFlushSeconds: 8) }, Overridable: overridable);

    [Fact]
    public void SettingsFor_gives_a_service_that_is_set_apart_its_own_and_every_other_service_those_of_the_agent()
    {
        // Arrange
        var configuration = Create();
        configuration.Apply(Central());

        // Act
        var orders = configuration.SettingsFor("orders");
        var other = configuration.SettingsFor("billing");

        // Assert
        orders.ShouldBe(new EffectiveSettings(60, 8));
        other.ShouldBe(new EffectiveSettings(20, 8));
    }

    [Fact]
    public void SettingsFor_ignores_a_local_override_unless_the_central_configuration_allows_that_setting()
    {
        // Arrange
        var configuration = new AgentConfiguration(NullLogger<AgentConfiguration>.Instance, new AgentConfigurationOptions { PullSeconds = 5, EdgeFlushSeconds = 3 });
        configuration.Apply(Central(overridable: ["pullSeconds"]));

        // Act
        var settings = configuration.SettingsFor("billing");

        // Assert: pull may be overridden here, the flush interval may not
        settings.ShouldBe(new EffectiveSettings(5, 8));
    }

    [Fact]
    public void SettingsFor_lets_an_allowed_local_override_win_over_the_setting_of_a_service()
    {
        // Arrange
        var configuration = new AgentConfiguration(NullLogger<AgentConfiguration>.Instance, new AgentConfigurationOptions { PullSeconds = 5 });
        configuration.Apply(Central(overridable: ["pullSeconds"]));

        // Act
        var settings = configuration.SettingsFor("orders");

        // Assert
        settings.PullSeconds.ShouldBe(5);
    }

    [Fact]
    public void SettingsFor_ignores_every_local_override_before_a_controller_has_spoken()
    {
        // Arrange
        var configuration = new AgentConfiguration(NullLogger<AgentConfiguration>.Instance, new AgentConfigurationOptions { PullSeconds = 5 });

        // Act
        var settings = configuration.SettingsFor("orders");

        // Assert
        settings.ShouldBe(new EffectiveSettings(15, 10));
    }

    [Fact]
    public void A_local_override_below_one_second_is_refused()
    {
        // Act
        var add = () => new ServiceCollection().AddAgentConfiguration(options => options.PullSeconds = 0);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

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
