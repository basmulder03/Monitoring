using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Agent.Features.Discovery;
using Plover.Agent.Features.Discovery.Windows;
using Plover.Agent.Features.ScopeState;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.Discovery.Windows;

public class WindowsDiscoveryTests
{
    private static WindowsServiceStatus Service(string name, WindowsServiceState state, WindowsServiceStartType start = WindowsServiceStartType.Automatic) =>
        new(name, $"{name} display", state, start);

    private sealed class FakeManager : IServiceControlManager
    {
        public List<WindowsServiceStatus> Services { get; } = [];

        public Exception? Failure { get; set; }

        public IReadOnlyList<WindowsServiceStatus> List() => Failure is null ? [.. Services] : throw Failure;
    }

    private sealed class Fixture
    {
        public Fixture(params string[] allowlist)
        {
            Store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "server-1" });
            Service = new WindowsDiscoveryService(
                Manager, Store, new WindowsDiscoveryOptions { Services = [.. allowlist] }, new FakeTimeProvider(), NullLogger<WindowsDiscoveryService>.Instance);
        }

        public FakeManager Manager { get; } = new();

        public ScopeStateStore Store { get; }

        public WindowsDiscoveryService Service { get; }

        public IReadOnlyList<InstanceInfo> Instances => Store.CreateSnapshot().Instances;

        public InstanceInfo Instance(string id) => Instances.Single(i => i.Id == id);
    }

    [Theory]
    [InlineData(WindowsServiceState.Running, InstanceLifecycle.Running, HealthState.Healthy)]
    [InlineData(WindowsServiceState.Paused, InstanceLifecycle.Running, HealthState.Degraded)]
    [InlineData(WindowsServiceState.StartPending, InstanceLifecycle.Starting, HealthState.Unknown)]
    [InlineData(WindowsServiceState.StopPending, InstanceLifecycle.Terminating, HealthState.Unknown)]
    [InlineData(WindowsServiceState.Stopped, InstanceLifecycle.Running, HealthState.Unhealthy)]
    public void The_state_of_a_service_decides_its_lifecycle_and_health(WindowsServiceState state, InstanceLifecycle lifecycle, HealthState health)
    {
        // Act
        var instance = WindowsServiceMapper.Map(Service("billing", state), restarts: 0);

        // Assert
        instance.Lifecycle.ShouldBe(lifecycle);
        instance.Health.ShouldBe(health);
    }

    [Theory]
    [InlineData(WindowsServiceStartType.Automatic, "should have it running")]
    [InlineData(WindowsServiceStartType.Disabled, "cannot be started")]
    [InlineData(WindowsServiceStartType.Manual, "start type is Manual")]
    public void A_stopped_service_is_unhealthy_with_the_cause_shown(WindowsServiceStartType start, string cause)
    {
        // Act
        var instance = WindowsServiceMapper.Map(Service("billing", WindowsServiceState.Stopped, start), restarts: 0);

        // Assert
        var check = instance.Checks!.Single();
        check.Name.ShouldBe("windows service");
        check.Health.ShouldBe(HealthState.Unhealthy);
        check.Description!.ShouldContain(cause);
    }

    [Fact]
    public void A_service_shows_its_name_labels_and_restart_count()
    {
        // Act
        var instance = WindowsServiceMapper.Map(Service("billing", WindowsServiceState.Running, WindowsServiceStartType.Manual), restarts: 2);

        // Assert
        instance.Id.ShouldBe("billing");
        instance.ServiceName.ShouldBe("billing");
        instance.Labels["windows.display-name"].ShouldBe("billing display");
        instance.Labels["windows.start-type"].ShouldBe("Manual");
        instance.Metrics!.Single(m => m.Name == WindowsServiceMapper.RestartsMetric).Value.ShouldBe(2);
        instance.Checks.ShouldBeNull();
    }

    [Fact]
    public void Only_the_services_on_the_allowlist_are_shown_by_exact_name_or_prefix_without_regard_to_case()
    {
        // Arrange
        var fixture = new Fixture("Billing", "sql*");
        fixture.Manager.Services.AddRange([
            Service("billing", WindowsServiceState.Running),
            Service("SQLWriter", WindowsServiceState.Running),
            Service("MSSQLSERVER", WindowsServiceState.Running),
            Service("Spooler", WindowsServiceState.Running),
        ]);

        // Act
        fixture.Service.Poll();

        // Assert
        fixture.Instances.Select(i => i.Id).Order().ShouldBe(["SQLWriter", "billing"]);
    }

    [Fact]
    public void A_service_that_stops_turns_unhealthy_and_one_that_is_removed_disappears()
    {
        // Arrange
        var fixture = new Fixture("billing", "reports");
        fixture.Manager.Services.AddRange([Service("billing", WindowsServiceState.Running), Service("reports", WindowsServiceState.Running)]);
        fixture.Service.Poll();

        // Act
        fixture.Manager.Services.Clear();
        fixture.Manager.Services.Add(Service("billing", WindowsServiceState.Stopped));
        fixture.Service.Poll();

        // Assert
        fixture.Instance("billing").Health.ShouldBe(HealthState.Unhealthy);
        fixture.Instances.Select(i => i.Id).ShouldBe(["billing"]);
    }

    [Fact]
    public void A_service_that_comes_back_after_not_running_is_a_restart_but_the_first_sighting_is_not()
    {
        // Arrange
        var fixture = new Fixture("billing");
        fixture.Manager.Services.Add(Service("billing", WindowsServiceState.Running));
        fixture.Service.Poll();
        var first = fixture.Instance("billing").Metrics!.Single().Value;

        // Act
        foreach (var state in new[] { WindowsServiceState.Stopped, WindowsServiceState.Running, WindowsServiceState.Running, WindowsServiceState.StopPending, WindowsServiceState.StartPending, WindowsServiceState.Running })
        {
            fixture.Manager.Services[0] = Service("billing", state);
            fixture.Service.Poll();
        }

        // Assert
        first.ShouldBe(0);
        fixture.Instance("billing").Metrics!.Single().Value.ShouldBe(2);
    }

    [Fact]
    public void A_failure_to_read_the_services_leaves_what_was_shown()
    {
        // Arrange
        var fixture = new Fixture("billing");
        fixture.Manager.Services.Add(Service("billing", WindowsServiceState.Running));
        fixture.Service.Poll();

        // Act
        fixture.Manager.Failure = new InvalidOperationException("access denied");
        fixture.Service.Poll();

        // Assert
        fixture.Instance("billing").Health.ShouldBe(HealthState.Healthy);
    }

    [Fact]
    public void Nothing_is_shown_when_the_allowlist_is_empty()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Manager.Services.Add(Service("billing", WindowsServiceState.Running));

        // Act
        fixture.Service.Poll();

        // Assert
        fixture.Instances.ShouldBeEmpty();
    }

    [Fact]
    public void Windows_discovery_is_refused_on_a_machine_that_is_not_windows()
    {
        // Act
        var add = () => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddDiscovery(options => options.Kind = DiscoveryKind.Windows);

        // Assert
        if (OperatingSystem.IsWindows())
        {
            add.ShouldNotThrow();
        }
        else
        {
            add.ShouldThrow<ArgumentException>();
        }
    }
}
