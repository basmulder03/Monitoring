// The smallest application that is monitored: one line adds the client, and everything else is an ordinary web application.
// It is also what the tests of the charts run, so it has a few things to observe: a health check you can break, and a call to another service.
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Retry;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddPlover(options =>
{
    options.ServiceName = builder.Configuration["SERVICE_NAME"] ?? "sample";
    options.Version = "1.0.0";
});

// Breaks when the file named by BROKEN_FILE exists, so a test can make the application unhealthy without restarting it.
var brokenFile = builder.Configuration["BROKEN_FILE"] ?? "/tmp/broken";
var brokenByDemo = false;
builder.Services.AddHealthChecks().AddCheck("switch", () =>
    brokenByDemo || File.Exists(brokenFile) ? HealthCheckResult.Unhealthy("switched off") : HealthCheckResult.Healthy());

var app = builder.Build();
// A package of its own, used for a retry around the call to the other service.
var retry = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 1 }).Build();
using var client = new HttpClient();

app.MapGet("/", () => "ok");

// For a live demo in a cluster, where the image has no shell to create the file in: DEMO_CONTROLS=1 lets the demo flip the same switch through the API server's pod proxy.
if (builder.Configuration["DEMO_CONTROLS"] == "1")
{
    app.MapGet("/demo/unhealthy", () => { brokenByDemo = true; return "unhealthy"; });
    app.MapGet("/demo/healthy", () => { brokenByDemo = false; return "healthy"; });
}

// Calls another service, which shows up as a dependency: /call?to=http://other:8080/
app.MapGet("/call", (string to) => CallAsync(to));

async Task<string> CallAsync(string to)
{
    var response = await retry.ExecuteAsync(async token => await client.GetAsync(to, token));
    return response.StatusCode.ToString();
}

// For a live demo: CALLS=http://payments:8080/,http://catalog:8080/ makes the service call those every few seconds, so there are calls between services without a queue or a database.
var targets = (builder.Configuration["CALLS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (targets.Length > 0)
{
    _ = Task.Run(async () =>
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(app.Lifetime.ApplicationStopping))
        {
            foreach (var target in targets)
            {
                try { await CallAsync(target); } catch (HttpRequestException) { /* the other service is down; that is what the demo shows */ }
            }
        }
    });
}

app.Run();
