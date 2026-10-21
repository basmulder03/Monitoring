using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Configuration;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Configuration;

public class ConfigurationReloadServiceTests
{
    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    [Fact]
    public async Task StartAsync_loads_the_file_before_anything_else_starts()
    {
        // Arrange
        using var file = new ConfigFile("""{ "defaults": { "pullSeconds": 30 } }""");
        var fixture = new ReloadFixture(file);

        // Act
        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        fixture.Configuration.For("shop", NoLabels).PullSeconds.ShouldBe(30);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_changed_file_is_picked_up_at_the_next_look()
    {
        // Arrange
        using var file = new ConfigFile("""{ "defaults": { "pullSeconds": 30 } }""");
        var fixture = new ReloadFixture(file);
        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);
        file.Write("""{ "defaults": { "pullSeconds": 40 } }""");

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await Eventually(() => fixture.Configuration.For("shop", NoLabels).PullSeconds == 40);

        // Assert
        fixture.Configuration.For("shop", NoLabels).PullSeconds.ShouldBe(40);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_file_that_is_not_valid_is_rejected_and_what_was_in_force_stays()
    {
        // Arrange
        using var file = new ConfigFile("""{ "defaults": { "pullSeconds": 30 } }""");
        var fixture = new ReloadFixture(file);
        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);

        // Act
        file.Write("""{ "defaults": { "pullSeconds": 0 } }""");
        fixture.Service.Load();

        // Assert
        fixture.Configuration.For("shop", NoLabels).PullSeconds.ShouldBe(30);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_file_that_is_invalid_at_startup_leaves_the_built_in_defaults_until_it_is_fixed()
    {
        // Arrange
        using var file = new ConfigFile("not json");
        var fixture = new ReloadFixture(file);
        await fixture.Service.StartAsync(TestContext.Current.CancellationToken);
        var before = fixture.Configuration.For("shop", NoLabels).PullSeconds;

        // Act
        file.Write("""{ "defaults": { "pullSeconds": 30 } }""");
        fixture.Service.Load();

        // Assert
        before.ShouldBe(15);
        fixture.Configuration.For("shop", NoLabels).PullSeconds.ShouldBe(30);
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_does_not_stop_the_service()
    {
        // Arrange
        var fixture = new ReloadFixture(new ConfigFile("{}", delete: true));

        // Act
        var start = async () => await fixture.Service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        await start.ShouldNotThrowAsync();
        await fixture.Service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_a_file_the_built_in_defaults_stay()
    {
        // Arrange
        var configuration = new EffectiveConfiguration(NullLogger<EffectiveConfiguration>.Instance);
        var service = new ConfigurationReloadService(
            configuration, new ConfigurationOptions(), new FakeTimeProvider(), NullLogger<ConfigurationReloadService>.Instance);

        // Act
        await service.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        configuration.For("shop", NoLabels).Version.ShouldStartWith("built-in");
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, guard.Token);
        }
    }

    private sealed class ReloadFixture
    {
        public ReloadFixture(ConfigFile file)
        {
            Configuration = new EffectiveConfiguration(NullLogger<EffectiveConfiguration>.Instance);
            Service = new ConfigurationReloadService(
                Configuration,
                new ConfigurationOptions { File = file.Path, PollInterval = TimeSpan.FromSeconds(5) },
                Clock,
                NullLogger<ConfigurationReloadService>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new();

        public EffectiveConfiguration Configuration { get; }

        public ConfigurationReloadService Service { get; }
    }

    private sealed class ConfigFile : IDisposable
    {
        public ConfigFile(string content, bool delete = false)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"central-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, content);
            if (delete)
            {
                File.Delete(Path);
            }
        }

        public string Path { get; }

        public void Write(string content) => File.WriteAllText(Path, content);

        public void Dispose() => File.Delete(Path);
    }
}
