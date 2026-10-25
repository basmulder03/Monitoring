using Plover.Client.Features.Packages;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Features.Packages;

public class PackageListTests
{
    [Fact]
    public void Parse_lists_every_package_ordered_by_name_and_marks_the_ones_the_project_asked_for()
    {
        // Arrange
        const string text = """
            D Zeta.Direct
            P Zeta.Direct 2.0.0
            P Alpha.Transitive 1.2.3
            P Mid.Package 4.5.6
            D Mid.Package
            """;

        // Act
        var report = PackageList.Parse(text);

        // Assert
        report.Packages.Select(p => p.Name).ShouldBe(["Alpha.Transitive", "Mid.Package", "Zeta.Direct"]);
        report.Packages.Select(p => p.Direct).ShouldBe([false, true, true]);
        report.Packages[0].Version.ShouldBe("1.2.3");
    }

    [Fact]
    public void Parse_keeps_the_first_version_of_a_package_listed_twice()
    {
        // Arrange
        const string text = "P Same.Package 1.0.0\nP Same.Package 2.0.0\n";

        // Act
        var report = PackageList.Parse(text);

        // Assert
        report.Packages.Single().Version.ShouldBe("1.0.0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("P only-a-name")]
    [InlineData("X a b")]
    public void Parse_skips_what_it_does_not_understand(string text)
    {
        // Arrange (the text comes from the test data)

        // Act
        var report = PackageList.Parse(text);

        // Assert
        report.Packages.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_accepts_windows_line_endings()
    {
        // Arrange
        const string text = "D A\r\nP A 1.0.0\r\n";

        // Act
        var report = PackageList.Parse(text);

        // Assert
        report.Packages.Single().ShouldBe(new PackageReport("A", "1.0.0", Direct: true));
    }

    [Fact]
    public void Read_gives_an_empty_list_for_an_application_built_without_it()
    {
        // Arrange
        var application = typeof(PackageListTests).Assembly;

        // Act
        var report = PackageList.Read(application);

        // Assert
        report.Packages.ShouldBeEmpty();
        report.Runtime.ShouldStartWith(".NET");
    }

    [Fact]
    public void Read_gives_an_empty_list_when_there_is_no_entry_assembly()
    {
        // Arrange (nothing to prepare)

        // Act
        var report = PackageList.Read(null);

        // Assert
        report.Packages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_pull_source_answers_with_the_list_as_json_and_keeps_the_cursor()
    {
        // Arrange
        var source = new PackagesPullSource();

        // Act
        var answer = await source.ReadAsync(3, TestContext.Current.CancellationToken);

        // Assert
        ReportPayload.ReadPackages(answer.Payload).ShouldNotBeNull();
        answer.Cursor.ShouldBe(3);
    }
}
