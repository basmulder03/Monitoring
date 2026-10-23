// Runs the whole system for a developer: dotnet run --project tests/Monitoring.AppHost, then open the dashboard of the controller from the Aspire dashboard.
// The specifications start the same environment with a run id of their own (--RunId=...), so runs on one machine do not meet.
using Monitoring.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddMonitoringEnvironment(runId: builder.Configuration["RunId"] ?? "dev");
builder.Build().Run();
