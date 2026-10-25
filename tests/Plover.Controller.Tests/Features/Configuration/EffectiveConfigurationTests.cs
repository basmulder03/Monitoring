using Microsoft.Extensions.Logging.Abstractions;
using Plover.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Configuration;

public class EffectiveConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private static EffectiveConfiguration Create() => new(NullLogger<EffectiveConfiguration>.Instance);

    [Fact]
    public void For_uses_the_built_in_defaults_until_a_configuration_is_given()
    {
        // Arrange
        var configuration = Create();

        // Act
        var settings = configuration.For("shop", NoLabels);

        // Assert
        settings.Version.ShouldStartWith("built-in");
    }

    [Fact]
    public void Replace_makes_the_new_configuration_count_and_tells_the_listeners()
    {
        // Arrange
        var configuration = Create();
        var told = 0;
        using var subscription = configuration.OnChanged(() => told++);

        // Act
        configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));

        // Assert
        configuration.For("shop", NoLabels).PullSeconds.ShouldBe(30);
        told.ShouldBe(1);
    }

    [Fact]
    public void Replace_says_nothing_when_the_content_is_the_same()
    {
        // Arrange
        var configuration = Create();
        configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));
        var told = 0;
        using var subscription = configuration.OnChanged(() => told++);

        // Act
        configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));

        // Assert
        told.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_has_been_disposed_is_no_longer_told()
    {
        // Arrange
        var configuration = Create();
        var told = 0;
        var subscription = configuration.OnChanged(() => told++);
        subscription.Dispose();

        // Act
        configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));

        // Assert
        told.ShouldBe(0);
    }

    [Fact]
    public void A_listener_that_fails_does_not_keep_the_others_from_hearing()
    {
        // Arrange
        var configuration = Create();
        var told = 0;
        using var failing = configuration.OnChanged(() => throw new InvalidOperationException("broken"));
        using var working = configuration.OnChanged(() => told++);

        // Act
        configuration.Replace(ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }"""));

        // Assert
        told.ShouldBe(1);
    }
}
