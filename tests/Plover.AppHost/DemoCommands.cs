using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Plover.AppHost;

/// <summary>
/// Buttons for a live demo: the commands that show next to every resource in the Aspire dashboard. They break the environment the way production breaks, and mend it again,
/// so a demo or a manual test needs no terminal. Stopping, starting and restarting the controller, the agent and the sidecars are buttons Aspire brings itself.
/// Only <c>Program.cs</c> adds them; the specifications start the environment without.
/// </summary>
public static class DemoCommands
{
    private static readonly HttpClient Http = new();

    /// <summary>Adds the buttons to the resources of <see cref="PloverEnvironment.AddPloverEnvironment"/>.</summary>
    /// <param name="builder">The builder of the environment, after the environment was added.</param>
    /// <param name="runId">The run id the environment was added with; the health switches live in its state folder.</param>
    public static void AddDemoCommands(this IDistributedApplicationBuilder builder, string runId)
    {
        var cluster = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == PloverEnvironment.Cluster);
        var pods = cluster.GetEndpoint("api");

        foreach (var name in PloverEnvironment.Applications.Concat(PloverEnvironment.Chain.Keys))
        {
            var app = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>().Single(r => r.Name == name));
            var broken = PloverEnvironment.BrokenSwitch(runId, name);
            var pod = $"{name}-1";

            // The health check of the application starts failing, and the instance turns unhealthy on the dashboard after the wait the controller is configured with.
            app.WithCommand("break", "Make unhealthy", _ => Do(() => File.WriteAllText(broken, "")), Options("The health check returns unhealthy.", () => !File.Exists(broken)));
            app.WithCommand("repair", "Make healthy", _ => Do(() => File.Delete(broken)), Options("The health check returns healthy again.", () => File.Exists(broken)));

            // What Kubernetes says about the pod, through the stand-in for the API server.
            app.WithCommand("crash", "Pod: crash loop", _ => Pod(HttpMethod.Put, pods, $"{pod}?service={name}&fault=crash"), Options("Kubernetes reports the application killed for lack of memory, again and again."));
            app.WithCommand("unready", "Pod: not ready", _ => Pod(HttpMethod.Put, pods, $"{pod}?service={name}&ready=false"), Options("Kubernetes reports the pod as not ready."));
            app.WithCommand("remove", "Pod: remove", _ => Pod(HttpMethod.Delete, pods, pod), Options("The pod disappears from the cluster."));
            app.WithCommand("restore", "Pod: restore", _ => Pod(HttpMethod.Put, pods, $"{pod}?service={name}"), Options("The pod is there and ready, without a fault."));
        }

        // Not reachable but not gone: the process keeps its state, nothing gets through. Stop is what takes a resource down.
        foreach (var name in new[] { "broker", "database" })
        {
            var container = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>().Single(r => r.Name == name));
            container.WithCommand("freeze", "Freeze (unreachable)", _ => Docker("pause", name), Options("docker pause: connections hang until the container is thawed."));
            container.WithCommand("thaw", "Thaw", _ => Docker("unpause", name), Options("docker unpause."));
        }
    }

    private static CommandOptions Options(string description, Func<bool>? enabled = null) => new()
    {
        Description = description,
        UpdateState = _ => enabled is null || enabled() ? ResourceCommandState.Enabled : ResourceCommandState.Disabled,
    };

    private static Task<ExecuteCommandResult> Do(Action action)
    {
        try
        {
            action();
            return Task.FromResult(CommandResults.Success());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(CommandResults.Failure(ex));
        }
    }

    private static async Task<ExecuteCommandResult> Pod(HttpMethod method, EndpointReference cluster, string path)
    {
        try
        {
            using var response = await Http.SendAsync(new HttpRequestMessage(method, $"{cluster.Url}/control/pods/{path}"));
            return response.IsSuccessStatusCode ? CommandResults.Success() : CommandResults.Failure($"The stand-in cluster answered {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            return CommandResults.Failure(ex);
        }
    }

    // Aspire names a container after its resource and a random suffix, so the filter matches the prefix.
    internal static async Task<ExecuteCommandResult> Docker(string verb, string resource)
    {
        var find = await Run("docker", "ps", "-q", "--filter", $"name=^{resource}-");
        var id = find.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (id is null)
        {
            return CommandResults.Failure($"No running container for {resource}.");
        }

        var result = await Run("docker", verb, id);
        return result.Exit == 0 ? CommandResults.Success() : CommandResults.Failure(result.Output);
    }

    private static async Task<(int Exit, string Output)> Run(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
