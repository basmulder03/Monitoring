using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Plover.AppHost;

/// <summary>
/// A real Windows server for the live demo, optional (<c>--Kind=true --Windows=true</c>): Windows in a virtual machine inside a container (dockur/windows, which needs KVM), with the agent
/// installed as a Windows service that reports to the controller of the kind cluster. Buttons stop and start Windows services and the agent over ssh, and freeze the whole machine.
/// The first start downloads Windows (about 6 GB, an evaluation version of Windows Server 2022) and installs it, which takes half an hour; the disk lives in a folder of your home, so the next start takes a minute or two.
/// </summary>
public static class WindowsEnvironment
{
    /// <summary>The token the agent in the VM introduces itself with; the controller holds its hash.</summary>
    public const string Token = "demo-windows-token";

    /// <summary>The services the agent reports on, besides itself.</summary>
    private static readonly string[] Services = ["Spooler", "W32Time"];

    /// <summary>Adds the preparation, the VM and its buttons.</summary>
    /// <param name="builder">The builder of the environment.</param>
    /// <param name="root">The root of the repository.</param>
    /// <param name="cluster">The cluster resource, which the VM waits for.</param>
    public static void AddWindowsEnvironment(this IDistributedApplicationBuilder builder, string root, IResourceBuilder<ExecutableResource> cluster)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/dev/kvm"))
        {
            throw new InvalidOperationException("The Windows VM needs Linux with KVM (/dev/kvm), which this machine does not have; on Windows itself, run the agent as a service with deploy/windows/install-agent.ps1.");
        }

        var output = Path.Combine(root, "artifacts", "demo", "windows");
        var key = Path.Combine(output, "id_ed25519");
        var storage = Environment.GetEnvironmentVariable("PLOVER_DEMO_WINDOWS_STORAGE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".plover-demo", "windows");
        Directory.CreateDirectory(storage);

        var prepare = builder.AddExecutable("windows-prepare", KindEnvironment.Bash, root, "deploy/demo/windows-prepare.sh").WithEnvironment("WINDOWS_TOKEN", Token).WaitFor(cluster);

        // On the docker network of kind, where the node of the cluster is. Published on this machine only: the viewer of the VM has no password, and ssh is by key.
        var windows = builder.AddContainer("windows", "dockurr/windows", "latest")
            .WithEnvironment("VERSION", "2022")
            .WithEnvironment("RAM_SIZE", "4G")
            .WithEnvironment("CPU_CORES", "2")
            .WithEnvironment("DISK_SIZE", "40G")
            .WithEnvironment("USERNAME", "plover")
            .WithEnvironment("PASSWORD", "Plover-demo-1")
            .WithBindMount(storage, "/storage")
            .WithBindMount(Path.Combine(output, "oem"), "/oem", isReadOnly: true)
            .WithBindMount(Path.Combine(output, "shared"), "/shared", isReadOnly: true)
            .WithContainerRuntimeArgs("--device=/dev/kvm", "--device=/dev/net/tun", "--cap-add=NET_ADMIN", "--stop-timeout=120", "--network=kind", "-p", "127.0.0.1:8006:8006", "-p", "127.0.0.1:2222:22")
            .WithUrl("http://127.0.0.1:8006", "Windows screen")
            .WaitForCompletion(prepare)
            .WaitFor(cluster);

        foreach (var service in Services)
        {
            windows.WithCommand($"stop-{service}", $"Stop {service}", _ => Ssh($"sc stop {service}", key), new CommandOptions { Description = $"The Windows service {service} stops; the agent reports it as unhealthy." });
            windows.WithCommand($"start-{service}", $"Start {service}", _ => Ssh($"sc start {service}", key), new CommandOptions { Description = $"The Windows service {service} starts." });
        }

        windows.WithCommand("kill-agent", "Kill agent process", _ => Ssh("taskkill /f /im Plover.Agent.exe", key), new CommandOptions { Description = "The service recovery of Windows restarts the agent after 5 seconds." });
        windows.WithCommand("stop-agent", "Stop agent service", _ => Ssh("sc stop PloverAgent", key), new CommandOptions { Description = "The server is no longer monitored; the scope turns unknown." });
        windows.WithCommand("start-agent", "Start agent service", _ => Ssh("sc start PloverAgent", key), new CommandOptions { Description = "The agent reports again." });
        windows.WithCommand("freeze", "Freeze server (unreachable)", _ => DemoCommands.Docker("pause", "windows"), new CommandOptions { Description = "docker pause: the whole machine hangs, as a server that dropped off the network." });
        windows.WithCommand("thaw", "Thaw server", _ => DemoCommands.Docker("unpause", "windows"), new CommandOptions { Description = "docker unpause." });
    }

    private static async Task<ExecuteCommandResult> Ssh(string command, string key)
    {
        var result = await KindEnvironment.Run("ssh", ["-i", key, "-p", "2222", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=no", "-o", "UserKnownHostsFile=/dev/null", "-o", "ConnectTimeout=10", "plover@127.0.0.1", command], null);
        return result.Exit == 0 ? CommandResults.Success() : CommandResults.Failure(result.Output);
    }
}
