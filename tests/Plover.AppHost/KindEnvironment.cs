using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Plover.AppHost;

/// <summary>
/// The live demo on a real Kubernetes: deploy/demo/kind-up.sh creates a kind cluster with the system in it and keeps the dashboard of the controller on a port; every other resource
/// here follows the logs of something in the cluster and carries the buttons that break it. The clusters stay after the host stops, so the next start is quick (deploy/demo/kind-down.sh deletes it).
/// </summary>
public static class KindEnvironment
{
    private const int DashboardPort = 18080;
    private static readonly string Kubeconfig = Environment.GetEnvironmentVariable("KUBECONFIG_DEMO") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "plover-demo.config");

    /// <summary>The applications of the cluster, in the namespace <c>shop</c>.</summary>
    public static readonly IReadOnlyList<string> Applications = ["orders", "payments", "catalog"];

    /// <summary>Adds the cluster and, per application and per part of the system, a resource with its buttons.</summary>
    /// <param name="builder">The builder of the environment.</param>
    /// <param name="root">The root of the repository.</param>
    /// <param name="windows">Whether a Windows machine is to report in too (<see cref="WindowsEnvironment"/>).</param>
    /// <returns>The cluster resource.</returns>
    public static IResourceBuilder<ExecutableResource> AddKindEnvironment(this IDistributedApplicationBuilder builder, string root, bool windows = false)
    {
        // Healthy when the dashboard answers, which is when everything before it is up; the resources below wait for that.
        var cluster = builder.AddExecutable("cluster", "bash", root, "deploy/demo/kind-up.sh")
            .WithEnvironment("SKIP_BUILD", builder.Configuration["SkipBuild"] ?? "0")
            .WithEnvironment("WINDOWS", windows ? "1" : "0")
            .WithEnvironment("WINDOWS_TOKEN", WindowsEnvironment.Token)
            .WithHttpEndpoint(port: DashboardPort, targetPort: DashboardPort, name: "web", isProxied: false)
            .WithHttpHealthCheck("/api/overview", endpointName: "web");

        foreach (var app in Applications)
        {
            var resource = Follow(builder, app, root, $"-n shop logs -f deploy/{app} --all-containers --prefix", cluster);
            resource.WithCommand("break", "Make unhealthy", _ => Pod(app, "demo/unhealthy"), Options("The health check of the application returns unhealthy."));
            resource.WithCommand("repair", "Make healthy", _ => Pod(app, "demo/healthy"), Options("The health check returns healthy again."));
            resource.WithCommand("kill", "Kill pod", _ => Kubectl($"delete pod -n shop -l app={app} --wait=false"), Options("Kubernetes starts a new pod; the dashboard shows the old one gone and the new one arriving."));
            resource.WithCommand("down", "Scale to zero", _ => Kubectl($"scale deployment/{app} -n shop --replicas=0"), Options("The service is completely down."));
            resource.WithCommand("up", "Scale to one", _ => Kubectl($"scale deployment/{app} -n shop --replicas=1"), Options("The service is back."));
            resource.WithCommand("rollout-bad", "Bad rollout", _ => Kubectl($"set image deployment/{app} -n shop {app}=does-not-exist:1"), Options("A new version that cannot start; the old pod keeps serving and the dashboard says a rollout is stuck."));
            resource.WithCommand("rollout-undo", "Undo rollout", _ => Kubectl($"rollout undo deployment/{app} -n shop"), Options("Back to the version that worked."));
        }

        var agent = Follow(builder, "agent", root, "-n shop logs -f deploy/plover-agent --prefix", cluster);
        agent.WithCommand("kill", "Kill agent pod", _ => Kubectl("delete pod -n shop -l app.kubernetes.io/name=plover-agent --wait=false"), Options("The agent restarts and the controller shows the scope unknown for a while."));
        agent.WithCommand("freeze", "Freeze (unreachable)", _ => Signal("STOP", "PLOVER_Agent__ScopeName="), Options("The process is stopped on the node: still there, but silent. The scope turns unknown."));
        agent.WithCommand("thaw", "Thaw", _ => Signal("CONT", "PLOVER_Agent__ScopeName="), Options("The agent carries on."));

        var controller = Follow(builder, "controller", root, "-n plover logs -f plover-controller-0 --prefix", cluster);
        foreach (var replica in new[] { 0, 1 })
        {
            controller.WithCommand($"kill-{replica}", $"Kill replica {replica}", _ => Kubectl($"delete pod plover-controller-{replica} -n plover --wait=false"), Options("The replica restarts and gets its state back from the agents; the other keeps serving."));
        }

        foreach (var replica in new[] { 0, 1 })
        {
            controller.WithCommand($"freeze-{replica}", $"Freeze replica {replica} (unreachable)", _ => Signal("STOP", $"HOSTNAME=plover-controller-{replica}$"), Options("The process is stopped on the node: the replica is there but answers nothing."));
            controller.WithCommand($"thaw-{replica}", $"Thaw replica {replica}", _ => Signal("CONT", $"HOSTNAME=plover-controller-{replica}$"), Options("The replica carries on."));
        }

        controller.WithCommand("stop", "Scale to zero", _ => Kubectl("scale statefulset/plover-controller -n plover --replicas=0"), Options("No controller at all."));
        controller.WithCommand("start", "Scale to two", _ => Kubectl("scale statefulset/plover-controller -n plover --replicas=2"), Options("Both replicas back."));
        return cluster;
    }

    // A log follower that starts again when the pod it follows was replaced, and waits for the cluster first.
    private static IResourceBuilder<ExecutableResource> Follow(IDistributedApplicationBuilder builder, string name, string root, string kubectlArguments, IResourceBuilder<ExecutableResource> cluster) =>
        builder.AddExecutable(name, "bash", root, "-c", $"while true; do kubectl {kubectlArguments} || true; sleep 2; done")
            .WithEnvironment("KUBECONFIG", Kubeconfig)
            .WaitFor(cluster);

    private static CommandOptions Options(string description) => new() { Description = description };

    // The flag goes through the pod proxy of the API server, so nothing is exposed and the image needs no shell.
    private static async Task<ExecuteCommandResult> Pod(string app, string path)
    {
        var name = await Run("kubectl", "-n", "shop", "get", "pod", "-l", $"app={app}", "-o", "jsonpath={.items[0].metadata.name}");
        return name.Exit != 0 || name.Output.Length == 0
            ? CommandResults.Failure($"No pod for {app}.")
            : await Kubectl($"get --raw /api/v1/namespaces/shop/pods/{name.Output.Trim()}:8080/proxy/{path}");
    }

    // kind's default network plugin does not enforce network policies, so "unreachable" is a stopped process: the processes of the pods are processes of the node container, found by their environment.
    private static async Task<ExecuteCommandResult> Signal(string signal, string environment)
    {
        var script = $"found=0; for e in /proc/[0-9]*/environ; do tr '\\0' '\\n' < $e 2>/dev/null | grep -q '^{environment}' && {{ p=${{e#/proc/}}; kill -{signal} ${{p%/environ}}; found=1; }}; done; [ $found = 1 ]";
        var result = await Run("docker", ["exec", "plover-demo-control-plane", "sh", "-c", script], null);
        return result.Exit == 0 ? CommandResults.Success() : CommandResults.Failure("No such process on the node.");
    }

    private static async Task<ExecuteCommandResult> Kubectl(string arguments, string? input = null)
    {
        var result = await Run("kubectl", [.. arguments.Split(' ')], input);
        return result.Exit == 0 ? CommandResults.Success() : CommandResults.Failure(result.Output);
    }

    private static Task<(int Exit, string Output)> Run(string file, params string[] arguments) => Run(file, arguments, null);

    internal static async Task<(int Exit, string Output)> Run(string file, string[] arguments, string? input)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        start.Environment["KUBECONFIG"] = Kubeconfig;
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
