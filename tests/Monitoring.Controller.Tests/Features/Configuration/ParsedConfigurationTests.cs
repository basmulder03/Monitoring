using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class ParsedConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    private const string WithServices = """
        {
          "defaults": { "pullSeconds": 15, "edgeFlushSeconds": 10 },
          "overrides": [
            { "match": { "labels": { "environment": "prod" } }, "set": { "pullSeconds": 10 } },
            { "match": { "service": "orders" }, "set": { "edgeFlushSeconds": 5 } },
            { "match": { "service": "reports" }, "set": { "pullSeconds": 120 } },
            { "match": { "scope": "shop", "service": "reports" }, "set": { "pullSeconds": 90 } }
          ],
          "overridable": ["pullSeconds"]
        }
        """;

    [Fact]
    public void For_sets_a_service_apart_with_the_settings_of_the_agent_where_the_service_says_nothing()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse(WithServices);

        // Act
        var settings = configuration.For("blog", new Dictionary<string, string> { ["environment"] = "prod" });

        // Assert: the labels apply to the agent, the service rules on top of that
        settings.PullSeconds.ShouldBe(10);
        settings.Services!["orders"].ShouldBe(new ServiceSettings(10, 5));
        settings.Services["reports"].ShouldBe(new ServiceSettings(120, 10));
    }

    [Fact]
    public void For_applies_a_rule_for_a_service_and_a_scope_only_to_the_agent_of_that_scope_and_after_the_one_for_the_service_alone()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse(WithServices);

        // Act
        var shop = configuration.For("shop", NoLabels);
        var blog = configuration.For("blog", NoLabels);

        // Assert
        shop.Services!["reports"].PullSeconds.ShouldBe(90);
        blog.Services!["reports"].PullSeconds.ShouldBe(120);
    }

    [Fact]
    public void For_leaves_out_a_service_whose_rules_change_nothing_for_it_and_has_no_services_at_all_without_such_rules()
    {
        // Arrange
        var same = ParsedConfiguration.Parse("""{ "defaults": { "pullSeconds": 15 }, "overrides": [ { "match": { "service": "orders" }, "set": { "pullSeconds": 15 } } ] }""");

        // Act
        var settings = same.For("shop", NoLabels);

        // Assert
        settings.Services.ShouldBeNull();
        ParsedConfiguration.BuiltIn.For("shop", NoLabels).Services.ShouldBeNull();
    }

    [Fact]
    public void For_gives_agents_that_get_different_service_settings_from_the_same_file_different_versions()
    {
        // Arrange
        var configuration = ParsedConfiguration.Parse(WithServices);

        // Act
        var shop = configuration.For("shop", NoLabels);
        var blog = configuration.For("blog", NoLabels);
        var blogAgain = configuration.For("blog", NoLabels);

        // Assert
        shop.Version.ShouldNotBe(blog.Version);
        blog.Version.ShouldBe(blogAgain.Version);
    }

    [Fact]
    public void For_passes_on_which_settings_may_be_overridden_locally()
    {
        // Act
        var settings = ParsedConfiguration.Parse(WithServices).For("shop", NoLabels);

        // Assert
        settings.Overridable.ShouldBe(["pullSeconds"]);
    }

    [Fact]
    public void Parse_accepts_a_rule_that_names_only_a_service_and_refuses_a_blank_one()
    {
        // Act
        var blank = () => ParsedConfiguration.Parse("""{ "overrides": [ { "match": { "service": " " }, "set": { "pullSeconds": 5 } } ] }""");
        var ok = () => ParsedConfiguration.Parse("""{ "overrides": [ { "match": { "service": "orders" }, "set": { "pullSeconds": 5 } } ] }""");

        // Assert
        blank.ShouldThrow<ConfigurationRejectedException>();
        ok.ShouldNotThrow();
    }

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
