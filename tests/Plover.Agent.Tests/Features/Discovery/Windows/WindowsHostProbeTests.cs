using Plover.Agent.Features.Discovery.Windows;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.Discovery.Windows;

/// <summary>Reads the real machine, so it only runs on Windows; everywhere else it says why it did not.</summary>
public class WindowsHostProbeTests
{
    [Fact]
    public void ReadHost_reports_the_machine_with_its_memory_disks_and_cpu_from_the_second_sample_on()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This reads a Windows machine.");
            return;
        }

        // Arrange
        var probe = new WindowsHostProbe();

        // Act
        var first = probe.ReadHost();
        Thread.Sleep(300);
        var second = probe.ReadHost();

        // Assert
        second.MachineName.ShouldBe(Environment.MachineName);
        second.OperatingSystem.ShouldContain("Windows");
        second.UptimeSeconds.ShouldBeGreaterThan(0);
        second.MemoryTotalBytes.ShouldBeGreaterThan(1L << 28);
        second.MemoryAvailableBytes.ShouldBeInRange(1, second.MemoryTotalBytes);
        second.Disks.ShouldContain(disk => disk.Name == "C:" && disk.TotalBytes > disk.FreeBytes && disk.FreeBytes > 0);
        first.CpuPercent.ShouldBeNull();
        second.CpuPercent.ShouldNotBeNull().ShouldBeInRange(0, 100);
    }

    [Fact]
    public void ReadProcess_reports_what_a_process_uses_and_nothing_for_one_that_does_not_exist()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("This reads a Windows machine.");
            return;
        }

        // Arrange
        var probe = new WindowsHostProbe();

        // Act
        var own = probe.ReadProcess(Environment.ProcessId);

        // Assert
        own.ShouldNotBeNull();
        own.MemoryBytes.ShouldBeGreaterThan(1_000_000);
        own.Threads.ShouldBeGreaterThan(1);
        own.Handles.ShouldBeGreaterThan(10);
        probe.ReadProcess(int.MaxValue - 1).ShouldBeNull();
    }

    [Fact]
    public void The_Service_Control_Manager_names_the_process_of_a_running_service_and_the_process_can_be_read()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Service Control Manager exists only on Windows.");
            return;
        }

        // Act
        var eventLog = new ServiceControlManager().List().Single(s => s.Name == "EventLog");

        // Assert
        eventLog.ProcessId.ShouldNotBeNull();
        new WindowsHostProbe().ReadProcess(eventLog.ProcessId.Value).ShouldNotBeNull();
        new ServiceControlManager().List().Where(s => s.State == WindowsServiceState.Stopped).ShouldAllBe(s => s.ProcessId == null);
    }
}
