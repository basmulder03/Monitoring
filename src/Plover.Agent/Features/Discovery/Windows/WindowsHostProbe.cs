using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Plover.Agent.Features.Discovery.Windows;

/// <summary>Reads the machine from Windows: the environment for what it knows, the system times for the processors and the registry for a pending restart.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsHostProbe : IHostProbe
{
    private (long Idle, long Total)? _lastCpu;

    /// <inheritdoc />
    public HostSample ReadHost()
    {
        var memory = ReadMemory();
        return new HostSample(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            Environment.TickCount64 / 1000,
            ReadCpu(),
            memory.Total,
            memory.Available,
            [.. DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => new DiskSample(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace))],
            IsRebootPending());
    }

    /// <inheritdoc />
    public ProcessSample? ReadProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessSample(process.WorkingSet64, process.Threads.Count, process.HandleCount);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process ended in the meantime, or belongs to an account this one may not look at.
            return null;
        }
    }

    /// <summary>The share of time the processors were not idle since the previous call.</summary>
    private double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        // Kernel time includes the idle time.
        var now = (Idle: idle, Total: kernel + user);
        var before = _lastCpu;
        _lastCpu = now;
        if (before is not { } previous || now.Total <= previous.Total)
        {
            return null;
        }

        var total = now.Total - previous.Total;
        return Math.Round(100.0 * (total - (now.Idle - previous.Idle)) / total, 1);
    }

    private static bool IsRebootPending()
    {
        using var machine = Registry.LocalMachine;
        return KeyExists(machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
            || KeyExists(machine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
            || machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager")?.GetValue("PendingFileRenameOperations") is not null;
    }

    private static bool KeyExists(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key is not null;
    }

    // What Windows says now; the memory load of the garbage collector only changes when a collection runs, so on an idle agent it stays at its first value.
    private static (long Total, long Available) ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return ((long)status.TotalPhys, (long)status.AvailPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
