// The smallest application that is monitored: one line adds the client, and everything else is an ordinary web application.
// It is also what the tests of the charts run, so it has a few things to observe: a health check you can break, and a call to another service.
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Retry;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddMonitoring(options =>
{
    options.ServiceName = builder.Configuration["SERVICE_NAME"] ?? "sample";
    options.Version = "1.0.0";
});

// Breaks when the file named by BROKEN_FILE exists, so a test can make the application unhealthy without restarting it.
var brokenFile = builder.Configuration["BROKEN_FILE"] ?? "/tmp/broken";
builder.Services.AddHealthChecks().AddCheck("switch", () =>
    File.Exists(brokenFile) ? HealthCheckResult.Unhealthy("switched off") : HealthCheckResult.Healthy());

var app = builder.Build();
// A package of its own, used for a retry around the call to the other service.
var retry = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1 }).Build();
using var client = new HttpClient();

app.MapGet("/", () => "ok");

// Calls another service, which shows up as a dependency: /call?to=http://other:8080/
app.MapGet("/call", (string to) => CallAsync(to));

async Task<string> CallAsync(string to)
{
    var response = await retry.ExecuteAsync(async token => await client.GetAsync(to, token));
    return response.StatusCode.ToString();
}

app.Run();
