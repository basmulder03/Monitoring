using System.Text.Json;
using Plover.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Vulnerabilities;

public class AdvisoryMatcherTests
{
    private static OsvAdvisory Advisory(string json) => JsonSerializer.Deserialize(json, OsvJsonContext.Default.OsvAdvisory)!;

    private static OsvAdvisory Ranged(string events, string package = "Serilog", string ecosystem = "NuGet", string type = "ECOSYSTEM") => Advisory($$"""
        { "id": "GHSA-1", "affected": [ { "package": { "ecosystem": "{{ecosystem}}", "name": "{{package}}" },
          "ranges": [ { "type": "{{type}}", "events": [ {{events}} ] } ] } ] }
        """);

    [Theory]
    [InlineData("3.0.0", false)]
    [InlineData("3.9.9", false)]
    [InlineData("4.0.0", true)]
    [InlineData("4.5.0", true)]
    [InlineData("4.9.9", true)]
    [InlineData("5.0.0", false)]
    [InlineData("6.1.0", false)]
    public void Match_affects_the_versions_between_introduced_and_fixed(string version, bool affected)
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "4.0.0" }, { "fixed": "5.0.0" }""");

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", version);

        // Assert
        result.ShouldBe(affected);
    }

    [Theory]
    [InlineData("0.5.0", true)]
    [InlineData("1.2.2", true)]
    [InlineData("1.2.3", false)]
    public void Match_affects_everything_before_the_fix_when_introduced_is_zero(string version, bool affected)
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "0" }, { "fixed": "1.2.3" }""");

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", version);

        // Assert
        result.ShouldBe(affected);
    }

    [Fact]
    public void Match_names_the_first_fixed_version_after_the_one_in_use()
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "0" }, { "fixed": "1.2.3" }""");

        // Act
        var (_, fixedIn) = AdvisoryMatcher.Match(advisory, "Serilog", "1.0.0");

        // Assert
        fixedIn.ShouldBe("1.2.3");
    }

    [Fact]
    public void Match_handles_several_stretches_in_one_range()
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "1.0.0" }, { "fixed": "1.5.0" }, { "introduced": "2.0.0" }, { "fixed": "2.3.0" }""");

        // Act
        var first = AdvisoryMatcher.Match(advisory, "Serilog", "1.2.0").Affected;
        var between = AdvisoryMatcher.Match(advisory, "Serilog", "1.7.0").Affected;
        var second = AdvisoryMatcher.Match(advisory, "Serilog", "2.1.0").Affected;
        var after = AdvisoryMatcher.Match(advisory, "Serilog", "2.3.0").Affected;

        // Assert
        (first, between, second, after).ShouldBe((true, false, true, false));
    }

    [Theory]
    [InlineData("1.9.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("2.0.1", false)]
    public void Match_stops_at_the_last_affected_version_for_a_problem_without_a_fix(string version, bool affected)
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "1.0.0" }, { "last_affected": "2.0.0" }""");

        // Act
        var (result, fixedIn) = AdvisoryMatcher.Match(advisory, "Serilog", version);

        // Assert
        result.ShouldBe(affected);
        fixedIn.ShouldBeNull();
    }

    [Fact]
    public void Match_affects_a_version_that_is_listed_by_name()
    {
        // Arrange
        var advisory = Advisory("""{ "id": "X", "affected": [ { "package": { "ecosystem": "NuGet", "name": "Serilog" }, "versions": [ "3.1.0", "3.1.1" ] } ] }""");

        // Act
        var listed = AdvisoryMatcher.Match(advisory, "Serilog", "3.1.1").Affected;
        var other = AdvisoryMatcher.Match(advisory, "Serilog", "3.2.0").Affected;

        // Assert
        listed.ShouldBeTrue();
        other.ShouldBeFalse();
    }

    [Fact]
    public void Match_ignores_the_case_of_the_package_name()
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "0" }, { "fixed": "9.0.0" }""", package: "SERILOG");

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "serilog", "1.0.0");

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("npm", "ECOSYSTEM")]
    [InlineData("PyPI", "ECOSYSTEM")]
    [InlineData("NuGet", "GIT")]
    public void Match_ignores_other_ecosystems_and_ranges_of_commits(string ecosystem, string type)
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "0" }""", ecosystem: ecosystem, type: type);

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", "1.0.0");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Match_ignores_another_package()
    {
        // Arrange
        var advisory = Ranged("""{ "introduced": "0" }""", package: "Json");

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", "1.0.0");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Match_ignores_an_advisory_that_says_nothing_about_packages()
    {
        // Arrange
        var advisory = Advisory("""{ "id": "X" }""");

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", "1.0.0");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Match_reads_the_snake_case_names_and_ignores_what_it_does_not_need()
    {
        // Arrange
        var advisory = Advisory("""
            { "id": "GHSA-9", "summary": "bad", "details": "long text", "schema_version": "1.6.0", "references": [ { "type": "WEB", "url": "https://x" } ],
              "database_specific": { "severity": "HIGH", "cwe_ids": ["CWE-79"] },
              "affected": [ { "package": { "ecosystem": "NuGet", "name": "Serilog", "purl": "pkg:nuget/Serilog" },
                "ranges": [ { "type": "ECOSYSTEM", "events": [ { "introduced": "0" }, { "last_affected": "1.0.0" } ] } ] } ] }
            """);

        // Act
        var (result, _) = AdvisoryMatcher.Match(advisory, "Serilog", "0.9.0");

        // Assert
        result.ShouldBeTrue();
        advisory.DatabaseSpecific!.Severity.ShouldBe("HIGH");
    }
}
