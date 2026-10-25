using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Plover.Agent.Features.Discovery.Windows;

/// <summary>Reads the services of the local machine from the Service Control Manager.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ServiceControlManager : IServiceControlManager
{
    /// <inheritdoc />
    public IReadOnlyList<WindowsServiceStatus> List()
    {
        var services = ServiceController.GetServices();
        try
        {
            return [.. services.Select(service => new WindowsServiceStatus(
                service.ServiceName, service.DisplayName, (WindowsServiceState)(int)service.Status, StartTypeOf(service), ProcessIdOf(service)))];
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    /// <summary>The process id of a running service, which the Service Control Manager knows and <see cref="ServiceController"/> does not say.</summary>
    private static int? ProcessIdOf(ServiceController service)
    {
        if (service.Status != ServiceControllerStatus.Running)
        {
            return null;
        }

        try
        {
            var handle = service.ServiceHandle;
            var found = QueryServiceStatusEx(handle.DangerousGetHandle(), ScStatusProcessInfo, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _);
            GC.KeepAlive(handle);
            return found && status.ProcessId != 0 ? (int)status.ProcessId : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A service this account may not open has no process to show.
            return null;
        }
    }

    private const int ScStatusProcessInfo = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int bytesNeeded);

    private static WindowsServiceStartType StartTypeOf(ServiceController service)
    {
        try
        {
            return (WindowsServiceStartType)(int)service.StartType;
        }
        catch (InvalidOperationException)
        {
            // Some system services do not let a service account read how they start; that is no reason to lose the service.
            return WindowsServiceStartType.Manual;
        }
    }
}
