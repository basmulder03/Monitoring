using Monitoring.Hosting;
using Monitoring.Sidecar.Hosting;

return await MonitoringHost.RunAsync(args, SidecarComposition.Compose);
