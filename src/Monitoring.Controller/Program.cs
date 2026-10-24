using Monitoring.Controller.Hosting;
using Monitoring.Hosting;

// --check builds the controller and looks at its configuration without starting it, and exits with 78 when something is wrong.
var (rest, check) = MonitoringHost.SplitCheck(args);
return await MonitoringHost.RunAsync(() => ControllerComposition.CreateHost(rest), check);
