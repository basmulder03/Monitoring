using Plover.Hosting;
using Plover.Injector.Hosting;

return await PloverHost.RunAsync(() => InjectorComposition.CreateHost(args));
