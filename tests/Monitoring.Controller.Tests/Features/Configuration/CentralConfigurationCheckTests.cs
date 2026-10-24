using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class CentralConfigurationCheckTests
{
    private static string File(string? text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"central-{Guid.NewGuid():N}.json");
        if (text is not null)
        {
            System.IO.File.WriteAllText(path, text);
        }

        return path;
    }

    [Fact]
    public void Without_a_file_there_is_nothing_to_check()
    {
        new CentralConfigurationCheck(new ConfigurationOptions()).Problems().ShouldBeEmpty();
    }

    [Fact]
    public void A_valid_file_has_no_problems()
    {
        // Arrange
        var path = File("""{ "defaults": { "pullSeconds": 15 } }""");

        // Act
        var problems = new CentralConfigurationCheck(new ConfigurationOptions { File = path }).Problems().ToList();

        // Assert
        problems.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json", "is not valid")]
    [InlineData("""{ "defaults": { "pullSeconds": 0 } }""", "is not valid")]
    [InlineData("""{ "overrides": [ { "match": {}, "set": {} } ] }""", "is not valid")]
    public void A_file_that_cannot_be_used_is_reported_with_its_name_and_the_reason(string text, string expected)
    {
        // Arrange
        var path = File(text);

        // Act
        var problem = new CentralConfigurationCheck(new ConfigurationOptions { File = path }).Problems().Single();

        // Assert
        problem.ShouldContain(path);
        problem.ShouldContain(expected);
    }

    [Fact]
    public void A_file_that_is_not_there_is_reported()
    {
        // Act
        var problem = new CentralConfigurationCheck(new ConfigurationOptions { File = File(null) }).Problems().Single();

        // Assert
        problem.ShouldContain("cannot be read");
    }
}
