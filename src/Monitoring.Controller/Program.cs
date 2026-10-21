using Monitoring.Controller.Hosting;
using Monitoring.Hosting;

return await MonitoringHost.RunAsync(() => ControllerComposition.CreateHost(args));
