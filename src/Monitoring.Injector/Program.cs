using Monitoring.Hosting;
using Monitoring.Injector.Hosting;

return await MonitoringHost.RunAsync(() => InjectorComposition.CreateHost(args));
