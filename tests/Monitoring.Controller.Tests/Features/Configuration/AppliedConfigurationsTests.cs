using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class AppliedConfigurationsTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private static (AppliedConfigurations Applied, EffectiveConfiguration Effective) Create()
    {
        var effective = new EffectiveConfiguration(NullLogger<EffectiveConfiguration>.Instance);
        return (new AppliedConfigurations(effective), effective);
    }

    [Fact]
    public void StatusOf_is_in_sync_when_the_agent_has_not_said_what_it_uses()
    {
        // Arrange
        var (applied, _) = Create();

        // Act
        var status = applied.StatusOf("shop", NoLabels);

        // Assert
        status.Applied.ShouldBeNull();
        status.InSync.ShouldBeTrue();
    }

    [Fact]
    public void StatusOf_is_in_sync_when_the_agent_uses_what_it_should()
    {
        // Arrange
        var (applied, effective) = Create();
        applied.Report("shop", Guid.NewGuid(), effective.For("shop", NoLabels).Version);

        // Act
        var status = applied.StatusOf("shop", NoLabels);

        // Assert
        status.InSync.ShouldBeTrue();
    }

    [Fact]
    public void StatusOf_is_out_of_sync_after_the_configuration_changed_and_the_agent_has_not_caught_up()
    {
        // Arrange
        var (applied, effective) = Create();
        applied.Report("shop", Guid.NewGuid(), effective.For("shop", NoLabels).Version);

        // Act
        effective.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));
        var status = applied.StatusOf("shop", NoLabels);

        // Assert
        status.InSync.ShouldBeFalse();
        status.Applied.ShouldStartWith("built-in");
        status.Expected.ShouldNotStartWith("built-in");
    }

    [Fact]
    public void StatusOf_judges_each_agent_by_the_settings_that_apply_to_it()
    {
        // Arrange
        var (applied, effective) = Create();
        effective.Replace(ParsedConfiguration.Parse("""{ "overrides": [ { "match": { "scope": "shop" }, "set": { "pullSeconds": 5 } } ] }"""));
        applied.Report("other", Guid.NewGuid(), effective.For("other", NoLabels).Version);

        // Act
        var status = applied.StatusOf("other", NoLabels);

        // Assert: an override for another scope does not make this agent out of date
        status.InSync.ShouldBeTrue();
    }

    [Fact]
    public void Disconnected_forgets_what_the_agent_said()
    {
        // Arrange
        var (applied, _) = Create();
        var connection = Guid.NewGuid();
        applied.Report("shop", connection, "old");

        // Act
        applied.Disconnected("shop", connection);

        // Assert
        applied.StatusOf("shop", NoLabels).Applied.ShouldBeNull();
    }

    [Fact]
    public void Disconnected_keeps_what_a_newer_connection_said()
    {
        // Arrange
        var (applied, _) = Create();
        var old = Guid.NewGuid();
        applied.Report("shop", old, "old");
        applied.Report("shop", Guid.NewGuid(), "new");

        // Act
        applied.Disconnected("shop", old);

        // Assert
        applied.StatusOf("shop", NoLabels).Applied.ShouldBe("new");
    }
}
