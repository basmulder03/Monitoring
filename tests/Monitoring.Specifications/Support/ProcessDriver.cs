using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Specifications.Support;

/// <summary>Stops and starts the processes of the environment, as an operator or a failure would.</summary>
public sealed class ProcessDriver
{
    private readonly HashSet<string> _stopped = [];

    /// <summary>Stops a process and waits until it has ended.</summary>
    /// <param name="process">The resource name.</param>
    /// <returns>A task that completes when it has ended.</returns>
    public async Task StopAsync(string process)
    {
        await CommandAsync(process, KnownResourceCommands.StopCommand, KnownResourceStates.Exited, KnownResourceStates.Finished);
        _stopped.Add(process);
    }

    /// <summary>Starts a process and waits until it runs.</summary>
    /// <param name="process">The resource name.</param>
    /// <returns>A task that completes when it runs.</returns>
    public async Task StartAsync(string process)
    {
        await CommandAsync(process, KnownResourceCommands.StartCommand, KnownResourceStates.Running);
        _stopped.Remove(process);
    }

    /// <summary>Starts everything this scenario stopped.</summary>
    /// <returns>A task that completes when they run.</returns>
    public async Task StartStoppedAsync()
    {
        foreach (var process in _stopped.ToList())
        {
            await StartAsync(process);
        }
    }

    private static async Task CommandAsync(string resource, string command, params string[] expectedStates)
    {
        var app = RunningEnvironment.App;
        var result = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(resource, command);
        if (!result.Success)
        {
            throw new InvalidOperationException($"'{command}' on '{resource}' failed: {result.Message}");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await app.ResourceNotifications.WaitForResourceAsync(resource, expectedStates, timeout.Token);
    }
}
