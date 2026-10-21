using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Reports;
using Monitoring.Sidecar.Features.LibraryLink;
using Monitoring.Sidecar.Features.Probing;
using Shouldly;
using Xunit;

namespace Monitoring.Sidecar.Tests.Features.Probing;

public class ProbeServiceTests
{
    [Fact]
    public void Current_says_the_application_cannot_be_reached_before_it_ever_answered()
    {
        // Arrange
        var fixture = new ProbeFixture();

        // Act
        var status = fixture.Service.Current;

        // Assert
        status.Description.ShouldBeNull();
        status.LastReached.ShouldBeNull();
        status.Health.Health.ShouldBe(HealthState.Unhealthy);
        status.Health.Checks.Single().Name.ShouldBe("application");
    }

    [Fact]
    public async Task Service_probes_at_once_when_it_starts()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders", "1.0.0"), HealthState.Degraded);

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Description is not null);

        // Assert
        var status = fixture.Service.Current;
        status.Description!.ServiceName.ShouldBe("orders");
        status.Health.Health.ShouldBe(HealthState.Degraded);
        status.LastReached.ShouldBe(fixture.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Service_probes_again_every_interval_and_follows_the_health_of_the_application()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders"), HealthState.Healthy);
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Health.Health == HealthState.Healthy && fixture.Link.HealthCalls == 1);

        // Act
        fixture.Link.Health = new HealthReport(HealthState.Unhealthy, [new HealthCheckResult("database", HealthState.Unhealthy, "down")]);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Health.Health == HealthState.Unhealthy);

        // Assert
        fixture.Service.Current.Health.Checks.Single().Description.ShouldBe("down");
    }

    [Fact]
    public async Task Service_reports_an_unreachable_application_as_unhealthy_and_remembers_who_it_was()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders", "1.0.0"), HealthState.Healthy);
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Description is not null);

        // Act
        fixture.Link.Unreachable = true;
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Health.Health == HealthState.Unhealthy);

        // Assert
        fixture.Service.Current.Description!.ServiceName.ShouldBe("orders");
    }

    [Fact]
    public async Task Service_keeps_the_time_the_application_was_last_reached_while_it_is_away()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders"), HealthState.Healthy);
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Service.Current.LastReached is not null);
        var reached = fixture.Service.Current.LastReached;

        // Act
        fixture.Link.Unreachable = true;
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Health.Health == HealthState.Unhealthy);

        // Assert
        fixture.Service.Current.LastReached.ShouldBe(reached);
    }

    [Fact]
    public async Task WaitForDescriptionAsync_completes_once_the_application_has_introduced_itself()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders", "3.0.0"), HealthState.Healthy);
        fixture.Link.Unreachable = true;
        await fixture.StartAsync();
        var waiting = fixture.Service.WaitForDescriptionAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        waiting.IsCompleted.ShouldBeFalse();

        // Act
        fixture.Link.Unreachable = false;
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var description = await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Assert
        description.Version.ShouldBe("3.0.0");
    }

    [Fact]
    public async Task WaitForDescriptionAsync_stops_when_asked_to()
    {
        // Arrange
        var fixture = new ProbeFixture();
        using var cancelled = new CancellationTokenSource();
        var waiting = fixture.Service.WaitForDescriptionAsync(cancelled.Token);

        // Act
        await cancelled.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await waiting);
    }

    [Fact]
    public async Task Service_picks_up_a_new_version_of_the_application()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders", "1.0.0"), HealthState.Healthy);
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Description is not null);

        // Act
        fixture.Link.Description = new InstanceDescription("orders", "1.1.0");
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Description!.Version == "1.1.0");

        // Assert
        fixture.Service.Current.Description!.Version.ShouldBe("1.1.0");
    }

    [Fact]
    public async Task Service_keeps_probing_after_an_unexpected_failure()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders"), HealthState.Healthy);
        fixture.Link.FailNextCall = true;
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Link.DescriptionCalls == 1);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitUntilAsync(() => fixture.Service.Current.Description is not null);

        // Assert
        fixture.Service.Current.Description!.ServiceName.ShouldBe("orders");
    }

    [Fact]
    public async Task Service_stops_when_the_host_stops()
    {
        // Arrange
        var fixture = new ProbeFixture().Answering(new InstanceDescription("orders"), HealthState.Healthy);
        await fixture.StartAsync();

        // Act
        var stop = async () => await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Builds the service over a scripted link and a fake clock.</summary>
    private sealed class ProbeFixture
    {
        public ProbeFixture()
        {
            Service = new ProbeService(Link, new ProbeOptions { Interval = TimeSpan.FromSeconds(5) }, Clock, NullLogger<ProbeService>.Instance);
        }

        public FakeTimeProvider Clock { get; } = new();

        public ScriptedLink Link { get; } = new();

        public ProbeService Service { get; }

        public ProbeFixture Answering(InstanceDescription description, HealthState health)
        {
            Link.Description = description;
            Link.Health = new HealthReport(health, []);
            return this;
        }

        public Task StartAsync() => Service.StartAsync(TestContext.Current.CancellationToken);

        public async Task WaitUntilAsync(Func<bool> condition)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                await Task.Delay(5, guard.Token);
            }
        }
    }

    /// <summary>A link whose answers the test controls.</summary>
    private sealed class ScriptedLink : ILibraryLink
    {
        private int _descriptionCalls;
        private int _healthCalls;

        public InstanceDescription Description { get; set; } = new("orders");

        public HealthReport Health { get; set; } = new(HealthState.Healthy, []);

        public bool Unreachable { get; set; }

        public bool FailNextCall { get; set; }

        public int DescriptionCalls => Volatile.Read(ref _descriptionCalls);

        public int HealthCalls => Volatile.Read(ref _healthCalls);

        public Task<InstanceDescription?> GetDescriptionAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _descriptionCalls);
            if (FailNextCall)
            {
                FailNextCall = false;
                throw new InvalidOperationException("unexpected");
            }

            return Task.FromResult<InstanceDescription?>(Unreachable ? null : Description);
        }

        public Task<HealthReport?> GetHealthAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _healthCalls);
            return Task.FromResult<HealthReport?>(Unreachable ? null : Health);
        }
    }
}
