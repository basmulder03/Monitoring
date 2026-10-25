using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Agent.Features.Discovery;
using Plover.Agent.Features.Discovery.Windows;
using Plover.Agent.Features.ScopeState;
using Plover.Protocol.Health;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.Discovery.Windows;

/// <summary>Talks to the real Service Control Manager, so it only runs on Windows; the CI of Windows runs it, and everywhere else it says why it did not.</summary>
public class ServiceControlManagerTests
{
    [Fact]
    public void List_returns_the_services_of_the_machine_with_their_state_and_start_type()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Service Control Manager exists only on Windows.");
            return;
        }

        // Act
        var services = new ServiceControlManager().List();

        // Assert: the event log service runs on every Windows machine
        var eventLog = services.Single(s => s.Name == "EventLog");
        eventLog.State.ShouldBe(WindowsServiceState.Running);
        eventLog.StartType.ShouldBe(WindowsServiceStartType.Automatic);
        eventLog.DisplayName.ShouldNotBeNullOrWhiteSpace();
        services.Count.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void Discovery_shows_a_running_service_as_healthy_and_a_stopped_one_as_unhealthy_with_the_cause()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Service Control Manager exists only on Windows.");
            return;
        }

        // Arrange: any service that is stopped on this machine will do
        var manager = new ServiceControlManager();
        var stopped = manager.List().First(s => s.State == WindowsServiceState.Stopped);
        var store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "server-1" });
        var discovery = new WindowsDiscoveryService(
            manager, store, new WindowsDiscoveryOptions { Services = ["EventLog", stopped.Name] }, new FakeTimeProvider(), NullLogger<WindowsDiscoveryService>.Instance);

        // Act
        discovery.Poll();

        // Assert
        var instances = store.CreateSnapshot().Instances;
        instances.Single(i => i.Id == "EventLog").Health.ShouldBe(HealthState.Healthy);
        var down = instances.Single(i => i.Id == stopped.Name);
        down.Health.ShouldBe(HealthState.Unhealthy);
        down.Checks!.Single().Description.ShouldNotBeNullOrWhiteSpace();
    }
}
