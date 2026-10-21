using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class ParsedConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    [Fact]
    public void For_uses_the_built_in_defaults_when_nothing_is_configured()
    {
        // Arrange
        var configuration = ParsedConfiguration.BuiltIn;

        // Act
        var settings = configuration.For("shop", NoLabels);

        // Assert
        settings.PullSeconds.ShouldBe(15);
        settings.EdgeFlushSeconds.ShouldBe(10);
        settings.Version.ShouldStartWith("built-in");
    }

    [Fact]
    public void For_applies_the_defaults_of_the_file_over_the_built_in_ones_for_what_it_sets()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }""");

        // Act
        var settings = configuration.For("shop", NoLabels);

        // Assert
        settings.PullSeconds.ShouldBe(30);
        settings.EdgeFlushSeconds.ShouldBe(10);
    }

    [Fact]
    public void For_applies_an_override_to_the_agents_with_the_labels_and_to_nobody_else()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse("""{ "overrides": [ { "match": { "labels": { "env": "prod" } }, "set": { "pullSeconds": 10 } } ] }""");

        // Act
        var prod = configuration.For("shop", new Dictionary<string, string> { ["env"] = "prod", ["site"] = "a" });
        var test = configuration.For("shop", new Dictionary<string, string> { ["env"] = "test" });
        var none = configuration.For("shop", NoLabels);

        // Assert
        prod.PullSeconds.ShouldBe(10);
        test.PullSeconds.ShouldBe(15);
        none.PullSeconds.ShouldBe(15);
    }

    [Fact]
    public void For_lets_a_rule_for_a_scope_win_over_a_rule_for_labels_whatever_their_order_in_the_file()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse("""
            { "overrides": [
              { "match": { "scope": "shop" }, "set": { "pullSeconds": 5 } },
              { "match": { "labels": { "env": "prod" } }, "set": { "pullSeconds": 10 } } ] }
            """);

        // Act
        var settings = configuration.For("shop", new Dictionary<string, string> { ["env"] = "prod" });

        // Assert
        settings.PullSeconds.ShouldBe(5);
    }

    [Fact]
    public void For_lets_a_later_rule_of_the_same_kind_win()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse("""
            { "overrides": [
              { "match": { "labels": { "env": "prod" } }, "set": { "pullSeconds": 10 } },
              { "match": { "labels": { "site": "a" } }, "set": { "pullSeconds": 20 } } ] }
            """);

        // Act
        var settings = configuration.For("shop", new Dictionary<string, string> { ["env"] = "prod", ["site"] = "a" });

        // Assert
        settings.PullSeconds.ShouldBe(20);
    }

    [Fact]
    public void For_requires_every_condition_of_a_rule_to_hold()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse("""{ "overrides": [ { "match": { "scope": "shop", "labels": { "env": "prod" } }, "set": { "pullSeconds": 5 } } ] }""");

        // Act
        var rightScopeWrongLabel = configuration.For("shop", new Dictionary<string, string> { ["env"] = "test" });
        var wrongScopeRightLabel = configuration.For("other", new Dictionary<string, string> { ["env"] = "prod" });

        // Assert
        rightScopeWrongLabel.PullSeconds.ShouldBe(15);
        wrongScopeRightLabel.PullSeconds.ShouldBe(15);
    }

    [Fact]
    public void For_changes_the_version_when_the_settings_of_that_agent_change_and_not_otherwise()
    {
        // Arrange
        var first = ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }""");
        var again = ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 30 } }""");
        var other = ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 31 } }""");

        // Act
        var a = first.For("shop", NoLabels).Version;
        var b = again.For("shop", NoLabels).Version;
        var c = other.For("shop", NoLabels).Version;

        // Assert
        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }

    [Fact]
    public void Parse_accepts_comments_and_trailing_commas()
    {
        // Arrange
        const string json = """
            {
              // faster in production
              "defaults": { "pullSeconds": 30, },
            }
            """;

        // Act
        var configuration = ParsedConfiguration.Parse(json);

        // Assert
        configuration.For("shop", NoLabels).PullSeconds.ShouldBe(30);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "defaults": { "pullSecond": 30 } }""")]
    [InlineData("""{ "defaults": { "pullSeconds": 0 } }""")]
    [InlineData("""{ "defaults": { "pullSeconds": 100000 } }""")]
    [InlineData("""{ "defaults": { "edgeFlushSeconds": -1 } }""")]
    [InlineData("""{ "overrides": [ { "match": {}, "set": { "pullSeconds": 5 } } ] }""")]
    [InlineData("""{ "overrides": [ { "set": { "pullSeconds": 5 } } ] }""")]
    [InlineData("""{ "overrides": [ { "match": { "scope": "x" } } ] }""")]
    [InlineData("""{ "overrides": [ { "match": { "scope": "x" }, "set": { "pullSeconds": 0 } } ] }""")]
    [InlineData("""{ "overridable": ["nonsense"] }""")]
    public void Parse_rejects_what_cannot_be_used_and_says_why(string json)
    {
        // Arrange (the text comes from the test data)

        // Act
        var parse = () => ParsedConfiguration.Parse(json);

        // Assert
        parse.ShouldThrow<ConfigurationRejectedException>().Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_names_the_place_of_a_value_that_is_out_of_range()
    {
        // Arrange
        const string json = """{ "overrides": [ { "match": { "scope": "x" }, "set": { "pullSeconds": 0 } } ] }""";

        // Act
        var parse = () => ParsedConfiguration.Parse(json);

        // Assert
        parse.ShouldThrow<ConfigurationRejectedException>().Message.ShouldContain("overrides[0].set.pullSeconds");
    }

    [Fact]
    public void Parse_accepts_the_known_overridable_settings()
    {
        // Arrange
        const string json = """{ "overridable": ["pullSeconds", "edgeFlushSeconds"] }""";

        // Act
        var parse = () => ParsedConfiguration.Parse(json);

        // Assert
        parse.ShouldNotThrow();
    }
}
