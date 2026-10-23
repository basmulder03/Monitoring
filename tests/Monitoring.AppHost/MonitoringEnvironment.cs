using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Monitoring.AppHost;

/// <summary>
/// The whole system as one environment: a controller, an agent, and applications that each have a sidecar, all as real processes that talk over the ports a real deployment uses.
/// A developer runs it to look at the dashboard; the specifications start the same environment and drive it, so what is run by hand and what is tested are one thing.
/// </summary>
public static class MonitoringEnvironment
{
    /// <summary>The token the agent introduces itself with; the controller holds its hash.</summary>
    public const string AgentToken = "e2e-agent-token";

    /// <summary>The scope the agent reports as.</summary>
    public const string Scope = "shop";

    /// <summary>The applications of the environment; <c>orders</c> calls <c>payments</c> when asked to.</summary>
    public static readonly IReadOnlyList<string> Applications = ["orders", "payments"];

    /// <summary>The name of the resource that is the controller.</summary>
    public const string Controller = "controller";

    /// <summary>The name of the resource that stands in for the Kubernetes API server.</summary>
    public const string Cluster = "cluster";

    /// <summary>The name of the resource that is the agent.</summary>
    public const string Agent = "agent";

    /// <summary>The folder in which the applications keep their sockets and their health switches, so a specification can break an application by touching a file.</summary>
    /// <param name="runId">Tells this run apart from others on the same machine.</param>
    /// <returns>The folder.</returns>
    public static string StateFolder(string runId) => Path.Combine(Path.GetTempPath(), $"monitoring-e2e-{runId}");

    /// <summary>The file whose existence makes an application unhealthy.</summary>
    /// <param name="runId">Tells this run apart from others on the same machine.</param>
    /// <param name="application">The application.</param>
    /// <returns>The path of the switch.</returns>
    public static string BrokenSwitch(string runId, string application) => Path.Combine(StateFolder(runId), $"{application}.broken");

    /// <summary>Adds the controller, the agent and the applications with their sidecars.</summary>
    /// <param name="builder">The builder of the environment.</param>
    /// <param name="runId">Tells this run apart from others on the same machine; the state folder is named after it.</param>
    /// <returns>The builder, for chaining.</returns>
    public static IDistributedApplicationBuilder AddMonitoringEnvironment(this IDistributedApplicationBuilder builder, string runId)
    {
        var state = StateFolder(runId);
        Directory.CreateDirectory(state);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AgentToken)));

        // The cluster the agent follows: one pod per application, named like the instance its sidecar registers as.
        var cluster = builder.AddProject<Projects.Monitoring_FakeCluster>(Cluster)
            .WithHttpEndpoint(name: "api")
            .WithEnvironment("FAKECLUSTER_PODS", string.Join(',', Applications.Select(application => $"{application}-1:{application}")));

        var controller = builder.AddProject<Projects.Monitoring_Controller>(Controller)
            .WithEndpoint(name: "agents", scheme: "tcp", env: "MONITORING_Controller__Listen__Port")
            .WithHttpEndpoint(name: "web", env: "MONITORING_Controller__Web__Port")
            .WithEnvironment("MONITORING_Controller__Listen__Address", "127.0.0.1")
            .WithEnvironment("MONITORING_Controller__Web__Address", "127.0.0.1")
            .WithEnvironment("MONITORING_Controller__AgentTokens__0__Scope", Scope)
            .WithEnvironment("MONITORING_Controller__AgentTokens__0__TokenHash", tokenHash)
            // A short grace period and quick liveness, so the specifications do not wait minutes for what a real deployment would.
            .WithEnvironment("MONITORING_Controller__Alerting__StartupGrace", "00:00:05")
            .WithEnvironment("MONITORING_Controller__Alerting__InstanceUnhealthyWait", "00:00:05")
            .WithEnvironment("MONITORING_Controller__Dashboard__PushInterval", "00:00:00.5");

        var agentsEndpoint = controller.GetEndpoint("agents");
        var agent = builder.AddProject<Projects.Monitoring_Agent>(Agent)
            .WithEndpoint(name: "sidecars", scheme: "tcp", env: "MONITORING_Agent__Sidecars__Listen__Port")
            .WithEnvironment("MONITORING_Agent__ScopeName", Scope)
            .WithEnvironment("MONITORING_Agent__Token", AgentToken)
            .WithEnvironment("MONITORING_Agent__Sidecars__Listen__Address", "127.0.0.1")
            .WithEnvironment("MONITORING_Agent__Sidecars__Enabled", "true")
            .WithEnvironment("MONITORING_Agent__Labels__environment", "e2e")
            .WithEnvironment("MONITORING_Agent__Discovery__Kind", "kubernetes")
            .WithEnvironment("MONITORING_Agent__Discovery__Kubernetes__Namespace", Scope)
            .WithEnvironment("MONITORING_Agent__Discovery__Kubernetes__Token", "cluster-token")
            .WithEnvironment("MONITORING_Agent__Discovery__Kubernetes__ApiServer", ReferenceExpression.Create($"{cluster.GetEndpoint("api").Property(EndpointProperty.Url)}"))
            .WithEnvironment("MONITORING_Agent__Controllers__0", ReferenceExpression.Create($"{agentsEndpoint.Property(EndpointProperty.IPV4Host)}:{agentsEndpoint.Property(EndpointProperty.Port)}"))
            .WaitFor(controller)
            .WaitFor(cluster);

        var sidecarsEndpoint = agent.GetEndpoint("sidecars");
        foreach (var application in Applications)
        {
            var socket = Path.Combine(state, $"{application}.sock");
            var app = builder.AddProject<Projects.Monitoring_SampleApp>(application)
                .WithHttpEndpoint(name: "http")
                .WithEnvironment("SERVICE_NAME", application)
                .WithEnvironment("MONITORING_SOCKET", socket)
                .WithEnvironment("BROKEN_FILE", BrokenSwitch(runId, application));

            builder.AddProject<Projects.Monitoring_Sidecar>($"{application}-sidecar")
                .WithEnvironment("MONITORING_Sidecar__Agent", ReferenceExpression.Create($"{sidecarsEndpoint.Property(EndpointProperty.IPV4Host)}:{sidecarsEndpoint.Property(EndpointProperty.Port)}"))
                .WithEnvironment("MONITORING_Sidecar__Library__Socket", socket)
                .WithEnvironment("MONITORING_Sidecar__InstanceId", $"{application}-1")
                .WaitFor(agent)
                .WaitFor(app);
        }

        return builder;
    }
}
