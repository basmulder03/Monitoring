using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Monitoring.AppHost;
using Reqnroll;

namespace Monitoring.Specifications.Support;

/// <summary>
/// The running system that every scenario talks to: started once for the whole run, because starting a controller, an agent and several sidecars costs seconds that a scenario should not repeat.
/// Scenarios leave it as they found it (see <see cref="Hooks"/>), which is why they do not run in parallel.
/// </summary>
[Binding]
public static class RunningEnvironment
{
    private static DistributedApplication? _app;

    /// <summary>The environment; only valid while scenarios run.</summary>
    public static DistributedApplication App => _app ?? throw new InvalidOperationException("The environment is not running.");

    /// <summary>The identity of this run, which names the folder with the health switches.</summary>
    public static string RunId { get; private set; } = "";

    /// <summary>Starts the controller, the agent and the applications, and waits until they run.</summary>
    [BeforeTestRun]
    public static async Task StartAsync()
    {
        RunId = Guid.NewGuid().ToString("N")[..8];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Monitoring_AppHost>([$"--RunId={RunId}"]);
        _app = await builder.BuildAsync();
        await _app.StartAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        foreach (var name in MonitoringEnvironment.Processes)
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

        var folder = MonitoringEnvironment.StateFolder(RunId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
