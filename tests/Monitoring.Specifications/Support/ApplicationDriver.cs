using Aspire.Hosting.Testing;
using Monitoring.AppHost;

namespace Monitoring.Specifications.Support;

/// <summary>The applications of the environment: what a user does to them (call them) and what an operator does to them (break them).</summary>
public sealed class ApplicationDriver
{
    private readonly HashSet<string> _broken = [];

    /// <summary>Makes the health check of an application fail, by creating the file it looks for.</summary>
    /// <param name="application">The application.</param>
    public void Break(string application)
    {
        File.WriteAllText(MonitoringEnvironment.BrokenSwitch(RunningEnvironment.RunId, application), "broken");
        _broken.Add(application);
    }

    /// <summary>Makes the health check of an application pass again.</summary>
    /// <param name="application">The application.</param>
    public void Repair(string application)
    {
        File.Delete(MonitoringEnvironment.BrokenSwitch(RunningEnvironment.RunId, application));
        _broken.Remove(application);
    }

    /// <summary>Repairs everything this scenario broke.</summary>
    public void RepairAll()
    {
        foreach (var application in _broken.ToList())
        {
            Repair(application);
        }
    }

    /// <summary>Asks an application for a path.</summary>
    /// <param name="application">The application.</param>
    /// <param name="path">The path and query.</param>
    /// <param name="times">How many times.</param>
    /// <returns>A task that completes when every answer was a success.</returns>
    public async Task GetAsync(string application, string path, int times = 1)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(application, "http");
        for (var call = 0; call < times; call++)
        {
            using var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Makes an application call another one, as a user's request would.</summary>
    /// <param name="caller">The application that is asked.</param>
    /// <param name="callee">The application it calls.</param>
    /// <param name="times">How many times.</param>
    /// <returns>A task that completes when the calls are done.</returns>
    public Task CallAsync(string caller, string callee, int times)
    {
        var target = RunningEnvironment.App.GetEndpoint(callee, "http");
        return GetAsync(caller, $"/call?to={Uri.EscapeDataString(target.ToString())}", times);
    }

    /// <summary>The port an application listens on, as a callee is addressed.</summary>
    /// <param name="application">The application.</param>
    /// <returns>The port.</returns>
    public static int PortOf(string application) => RunningEnvironment.App.GetEndpoint(application, "http").Port;
}
