using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Vulnerabilities;

public class AdvisoryStoreTests
{
    private const string One = """{ "id": "GHSA-1" }""";
    private const string Two = """{ "id": "GHSA-2" }""";

    private static (AdvisoryStore Store, string Path) Create(string path) =>
        (new AdvisoryStore(new VulnerabilitiesOptions { Advisories = path }, new FakeTimeProvider(), NullLogger<AdvisoryStore>.Instance), path);

    [Fact]
    public void Refresh_reads_a_file_with_one_advisory_or_a_list()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"adv-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, $"[{One}, {Two}]");
        var (store, _) = Create(file);

        // Act
        store.Refresh();

        // Assert
        store.Current.Advisories.Select(a => a.Id).ShouldBe(["GHSA-1", "GHSA-2"]);
        File.Delete(file);
    }

    [Fact]
    public void Refresh_reads_every_json_file_of_a_folder_including_subfolders()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("adv-").FullName;
        Directory.CreateDirectory(Path.Combine(folder, "2026"));
        File.WriteAllText(Path.Combine(folder, "a.json"), One);
        File.WriteAllText(Path.Combine(folder, "2026", "b.json"), Two);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not an advisory");
        var (store, _) = Create(folder);

        // Act
        store.Refresh();

        // Assert
        store.Current.Advisories.Select(a => a.Id).ShouldBe(["GHSA-2", "GHSA-1"], ignoreOrder: true);
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Refresh_picks_up_a_change_of_the_file()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"adv-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, One);
        var (store, _) = Create(file);
        store.Refresh();

        // Act
        File.WriteAllText(file, $"[{One}, {Two}]");
        store.Refresh();

        // Assert
        store.Current.Advisories.Count.ShouldBe(2);
        File.Delete(file);
    }

    [Fact]
    public void Refresh_keeps_what_was_loaded_when_the_new_content_cannot_be_read()
    {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"adv-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, One);
        var (store, _) = Create(file);
        store.Refresh();

        // Act
        File.WriteAllText(file, "{ this is not json");
        store.Refresh();

        // Assert
        store.Current.Advisories.Single().Id.ShouldBe("GHSA-1");
        File.Delete(file);
    }

    [Fact]
    public void Refresh_does_nothing_without_a_path_or_when_the_path_does_not_exist()
    {
        // Arrange
        var nothing = new AdvisoryStore(new VulnerabilitiesOptions(), new FakeTimeProvider(), NullLogger<AdvisoryStore>.Instance);
        var (missing, _) = Create(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));

        // Act
        nothing.Refresh();
        missing.Refresh();

        // Assert
        nothing.Current.Advisories.ShouldBeEmpty();
        missing.Current.Advisories.ShouldBeEmpty();
    }
}
