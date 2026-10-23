using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.Discovery.Windows;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Windows;

public class WindowsHostTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static HostSample Host(double? cpu = 12.5, params DiskSample[] disks) =>
        new("SRV-1", "Microsoft Windows 10.0.26100", 3600, cpu, 16 * Gb, 4 * Gb, disks.Length > 0 ? disks : [new DiskSample("C:", 100 * Gb, 50 * Gb)], PendingReboot: false);

    [Fact]
    public void The_host_shows_as_an_instance_of_its_own_with_what_the_machine_reports()
    {
        // Act
        var instance = HostMapper.Map(Host());

        // Assert
        instance.Id.ShouldBe("SRV-1");
        instance.ServiceName.ShouldBe(HostMapper.ServiceName);
        instance.Health.ShouldBe(HealthState.Healthy);
        instance.Labels["windows.os"].ShouldContain("Windows");
        var metrics = instance.Metrics!.ToDictionary(m => m.Name + string.Join("", m.Labels.Select(l => $"[{l.Value}]")), m => m.Value);
        metrics["host.uptime_seconds"].ShouldBe(3600);
        metrics["host.cpu_percent"].ShouldBe(12.5);
        metrics["host.memory_available_bytes"].ShouldBe(4 * Gb);
        metrics["host.disk_free_bytes[C:]"].ShouldBe(50 * Gb);
        metrics["host.pending_reboot"].ShouldBe(0);
    }

    [Fact]
    public void The_first_sample_has_no_cpu_figure_because_it_has_nothing_to_compare_with()
    {
        // Act
        var instance = HostMapper.Map(Host(cpu: null));

        // Assert
        instance.Metrics!.ShouldNotContain(m => m.Name == "host.cpu_percent");
    }

    [Theory]
    [InlineData(8, HealthState.Degraded)]
    [InlineData(2, HealthState.Unhealthy)]
    [InlineData(40, HealthState.Healthy)]
    public void A_disk_that_is_nearly_full_makes_the_host_degraded_and_then_unhealthy(int freePercent, HealthState expected)
    {
        // Act
        var instance = HostMapper.Map(Host(disks: new DiskSample("D:", 100 * Gb, freePercent * Gb)));

        // Assert
        instance.Health.ShouldBe(expected);
        if (expected != HealthState.Healthy)
        {
            instance.Checks!.Single().Name.ShouldBe("disk D:");
        }
    }

    private sealed class FakeProbe : IHostProbe
    {
        public int HostReads { get; private set; }

        public HostSample ReadHost()
        {
            HostReads++;
            return Host();
        }

        public ProcessSample? ReadProcess(int processId) => processId == 4242 ? new ProcessSample(100_000_000, 12, 300) : null;
    }

    private sealed class FakeManager(params WindowsServiceStatus[] services) : IServiceControlManager
    {
        public IReadOnlyList<WindowsServiceStatus> List() => services;
    }

    private sealed class Fixture
    {
        public Fixture(params WindowsServiceStatus[] services)
        {
            Store = new ScopeStateStore(new ScopeStateOptions { ScopeName = "server-1" });
            Service = new WindowsDiscoveryService(
                new FakeManager(services), Store, new WindowsDiscoveryOptions { Services = ["billing", "reports"], MeasureInterval = TimeSpan.FromSeconds(30) },
                Clock, NullLogger<WindowsDiscoveryService>.Instance, Probe);
        }

        public FakeTimeProvider Clock { get; } = new();

        public FakeProbe Probe { get; } = new();

        public ScopeStateStore Store { get; }

        public WindowsDiscoveryService Service { get; }

        public InstanceInfo Instance(string id) => Store.CreateSnapshot().Instances.Single(i => i.Id == id);
    }

    [Fact]
    public void The_host_and_the_process_of_a_running_service_are_shown_even_though_the_host_is_not_on_the_allowlist()
    {
        // Arrange
        var fixture = new Fixture(
            new WindowsServiceStatus("billing", "Billing", WindowsServiceState.Running, WindowsServiceStartType.Automatic, ProcessId: 4242),
            new WindowsServiceStatus("reports", "Reports", WindowsServiceState.Stopped, WindowsServiceStartType.Automatic));

        // Act
        fixture.Service.Poll();

        // Assert
        fixture.Instance("SRV-1").ServiceName.ShouldBe("windows-host");
        var billing = fixture.Instance("billing").Metrics!.ToDictionary(m => m.Name, m => m.Value);
        billing["windows.process.memory_bytes"].ShouldBe(100_000_000);
        billing["windows.process.threads"].ShouldBe(12);
        billing["windows.process.handles"].ShouldBe(300);
        fixture.Instance("reports").Metrics!.ShouldNotContain(m => m.Name.StartsWith("windows.process", StringComparison.Ordinal));
    }

    [Fact]
    public void The_machine_is_measured_on_its_own_slower_interval()
    {
        // Arrange
        var fixture = new Fixture(new WindowsServiceStatus("billing", "Billing", WindowsServiceState.Running, WindowsServiceStartType.Automatic, ProcessId: 4242));
        fixture.Service.Poll();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Service.Poll();
        var soon = fixture.Probe.HostReads;
        fixture.Clock.Advance(TimeSpan.FromSeconds(25));
        fixture.Service.Poll();

        // Assert
        soon.ShouldBe(1);
        fixture.Probe.HostReads.ShouldBe(2);
    }
}
