using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Monitoring.AppHost;
using Reqnroll;

namespace Monitoring.Specifications.Support;

/// <summary>
/// The running system that every scenario talks to: started once for the whole run, because starting a controller, an agent and several sidecars costs seconds that a scenario should not repeat.
/// Scenarios leave it as they found it, which is why they do not run in parallel.
/// </summary>
[Binding]
public static class RunningEnvironment
{
    private static DistributedApplication? _app;
    private static string _runId = "";

    /// <summary>The environment; only valid while scenarios run.</summary>
    public static DistributedApplication App => _app ?? throw new InvalidOperationException("The environment is not running.");

    /// <summary>The identity of this run, which names the folder with the health switches.</summary>
    public static string RunId => _runId;

    /// <summary>Starts the controller, the agent and the applications, and waits until they run.</summary>
    [BeforeTestRun]
    public static async Task StartAsync()
    {
        _runId = Guid.NewGuid().ToString("N")[..8];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Monitoring_AppHost>([$"--RunId={_runId}"]);
        _app = await builder.BuildAsync();
        await _app.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        foreach (var name in new[] { MonitoringEnvironment.Cluster, MonitoringEnvironment.Controller, MonitoringEnvironment.Agent }.Concat(MonitoringEnvironment.Applications))
        {
            await _app.ResourceNotifications.WaitForResourceAsync(name, KnownResourceStates.Running, timeout.Token);
        }
    }

    /// <summary>Stops everything and removes what the run left on disk.</summary>
    [AfterTestRun]
    public static async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        var folder = MonitoringEnvironment.StateFolder(_runId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Reads a page of the API of the controller, which is what the dashboard shows.</summary>
    /// <param name="path">The path, such as <c>/api/scopes/shop</c>.</param>
    /// <returns>The JSON, or <see langword="null"/> when the controller does not have it (yet).</returns>
    public static async Task<JsonDocument?> GetJsonAsync(string path)
    {
        // A controller that is down leaves its port open for a while, so a request must not wait for the default hundred seconds.
        using var client = App.CreateHttpClient(MonitoringEnvironment.Controller, "web");
        client.Timeout = TimeSpan.FromSeconds(5);
        using var response = await client.GetAsync(path);
        return response.IsSuccessStatusCode ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()) : null;
    }
}
