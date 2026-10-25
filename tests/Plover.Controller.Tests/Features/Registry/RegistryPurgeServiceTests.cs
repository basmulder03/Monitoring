using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Registry;
using Plover.Controller.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Registry;

public class RegistryPurgeServiceTests
{
    [Fact]
    public async Task Service_purges_the_registry_every_interval()
    {
        // Arrange
        var fixture = new PurgeFixture().WithInterval(TimeSpan.FromMinutes(1));
        await fixture.StartAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForPurgesAsync(1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForPurgesAsync(2);

        // Assert
        fixture.Registry.PurgeCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Service_does_not_purge_before_the_first_interval_has_passed()
    {
        // Arrange
        var fixture = new PurgeFixture().WithInterval(TimeSpan.FromMinutes(1));
        await fixture.StartAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.Registry.PurgeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Service_keeps_purging_after_a_purge_failed()
    {
        // Arrange
        var fixture = new PurgeFixture().WithInterval(TimeSpan.FromMinutes(1));
        fixture.Registry.WithFailingNextPurge();
        await fixture.StartAsync();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForPurgesAsync(1);

        // Act
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await fixture.WaitForPurgesAsync(2);

        // Assert
        fixture.Registry.PurgeCalls.ShouldBe(2);
    }

    [Fact]
    public async Task Service_stops_when_the_host_stops()
    {
        // Arrange
        var fixture = new PurgeFixture();
        await fixture.StartAsync();

        // Act
        var stop = async () => await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Builds the service over a fake registry and a fake clock.</summary>
    private sealed class PurgeFixture
    {
        private readonly RegistryOptions _options = new();

        public FakeTimeProvider Clock { get; } = new();

        public FakeScopeRegistry Registry { get; } = new();

        public RegistryPurgeService Service { get; private set; } = null!;

        public PurgeFixture WithInterval(TimeSpan interval)
        {
            _options.PurgeInterval = interval;
            return this;
        }

        /// <summary>Starts the service; its timer exists by the time this returns.</summary>
        public async Task StartAsync()
        {
            Service = new RegistryPurgeService(Registry, _options, Clock, NullLogger<RegistryPurgeService>.Instance);
            await Service.StartAsync(TestContext.Current.CancellationToken);
        }

        public async Task WaitForPurgesAsync(int count)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (Registry.PurgeCalls < count)
            {
                await Task.Delay(5, guard.Token);
            }
        }
    }
}
