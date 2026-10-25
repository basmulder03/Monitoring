using Plover.Controller.Hosting;
using Plover.Hosting;

// --check builds the controller and looks at its configuration without starting it, and exits with 78 when something is wrong.
var (rest, check) = PloverHost.SplitCheck(args);
return await PloverHost.RunAsync(() => ControllerComposition.CreateHost(rest), check);
