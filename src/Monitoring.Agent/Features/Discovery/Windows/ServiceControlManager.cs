using System.ServiceProcess;

namespace Monitoring.Agent.Features.Discovery.Windows;

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
                service.ServiceName, service.DisplayName, (WindowsServiceState)(int)service.Status, StartTypeOf(service)))];
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

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
