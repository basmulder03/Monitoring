using Monitoring.Controller.Hosting;
using Monitoring.Hosting;

return await MonitoringHost.RunAsync(args, ControllerComposition.Compose);
