// Runs the whole system for a developer or a live demo, with buttons in the Aspire dashboard that break and mend it (DemoCommands): dotnet run --project tests/Plover.AppHost, then open the dashboard of the controller from the Aspire dashboard.
// The specifications start the same environment with a run id of their own (--RunId=...), so runs on one machine do not meet.
using Plover.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var runId = builder.Configuration["RunId"] ?? "dev";
if (builder.Configuration["Kind"] == "true")
{
    // On a real Kubernetes (kind) instead of processes: dotnet run --project tests/Plover.AppHost -- --Kind=true
    // With --Windows=true a Windows server in a VM reports to the controller too (WindowsEnvironment; downloads Windows the first time).
    var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
    var windows = builder.Configuration["Windows"] == "true";
    var cluster = builder.AddKindEnvironment(root, windows);
    if (windows)
    {
        builder.AddWindowsEnvironment(root, cluster);
    }
}
else
{
    if (builder.Configuration["Windows"] == "true")
    {
        throw new InvalidOperationException("--Windows=true needs --Kind=true: the Windows server reports to the controller in the kind cluster.");
    }

    builder.AddPloverEnvironment(runId);
    builder.AddDemoCommands(runId);
}
builder.Build().Run();
