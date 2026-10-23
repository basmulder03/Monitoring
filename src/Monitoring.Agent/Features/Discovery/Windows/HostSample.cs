namespace Monitoring.Agent.Features.Discovery.Windows;

/// <summary>What the machine says about itself at one moment.</summary>
/// <param name="MachineName">The name of the machine.</param>
/// <param name="OperatingSystem">The version of the operating system, as a person reads it.</param>
/// <param name="UptimeSeconds">How long the machine has been running.</param>
/// <param name="CpuPercent">How busy the processors were since the previous sample; <see langword="null"/> for the first sample, which has nothing to compare with.</param>
/// <param name="MemoryTotalBytes">The physical memory of the machine.</param>
/// <param name="MemoryAvailableBytes">The physical memory that is free.</param>
/// <param name="Disks">The fixed disks.</param>
/// <param name="PendingReboot">Whether Windows is waiting for a restart to finish an update or an installation.</param>
internal sealed record HostSample(
    string MachineName,
    string OperatingSystem,
    long UptimeSeconds,
    double? CpuPercent,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    IReadOnlyList<DiskSample> Disks,
    bool PendingReboot);

/// <summary>The space on one disk.</summary>
/// <param name="Name">The drive, for example <c>C:</c>.</param>
/// <param name="TotalBytes">The size of the disk.</param>
/// <param name="FreeBytes">The space that is free.</param>
internal sealed record DiskSample(string Name, long TotalBytes, long FreeBytes);

/// <summary>What one process uses.</summary>
/// <param name="MemoryBytes">The working set of the process.</param>
/// <param name="Threads">The number of threads.</param>
/// <param name="Handles">The number of handles.</param>
internal sealed record ProcessSample(long MemoryBytes, int Threads, int Handles);
