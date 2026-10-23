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

    /// <summary>The call chain: <c>testapi</c> puts a message on a queue, <c>testworker</c> takes it and asks <c>testdata</c>, which asks a database. The value is the role of the program.</summary>
    public static readonly IReadOnlyDictionary<string, string> Chain = new Dictionary<string, string> { ["testapi"] = "api", ["testworker"] = "worker", ["testdata"] = "data" };

    /// <summary>Every process of the environment that the scenarios wait for.</summary>
    public static IEnumerable<string> Processes => new[] { Cluster, Controller, Agent }.Concat(Applications).Concat(Chain.Keys);

    /// <summary>The queue the chain uses.</summary>
    public const string Queue = "orders";

    /// <summary>The name of the resource that is the controller.</summary>
    public const string Controller = "controller";

    /// <summary>The token that lets a person or a wall display see the dashboard; the controller holds its hash.</summary>
    public const string DashboardToken = "e2e-dashboard-token-0123456789";

    /// <summary>The secret the controller signs the alert webhook with.</summary>
    public const string WebhookSecret = "e2e-webhook-secret";

    /// <summary>The name of the resource that stands in for the Kubernetes API server.</summary>
    public const string Cluster = "cluster";

    /// <summary>The name of the resource that is the agent.</summary>
    public const string Agent = "agent";

    /// <summary>The folder in which the applications keep their sockets and their health switches, so a specification can break an application by touching a file.</summary>
    /// <param name="runId">Tells this run apart from others on the same machine.</param>
    /// <returns>The folder.</returns>
    public static string StateFolder(string runId) => Path.Combine(Path.GetTempPath(), $"monitoring-e2e-{runId}");

    /// <summary>What the central configuration file holds when nothing has changed it.</summary>
    public const string DefaultCentralConfiguration = "{ \"defaults\": { \"pullSeconds\": 15, \"edgeFlushSeconds\": 10 } }";

    /// <summary>The central configuration file of the controller, which a specification can rewrite to see the change reach the agent.</summary>
    /// <param name="runId">Tells this run apart from others on the same machine.</param>
    /// <returns>The path of the file.</returns>
    public static string CentralConfiguration(string runId) => Path.Combine(StateFolder(runId), "central.json");

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
        File.WriteAllText(CentralConfiguration(runId), DefaultCentralConfiguration);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AgentToken)));

        // The cluster the agent follows: one pod per application, named like the instance its sidecar registers as.
        var cluster = builder.AddProject<Projects.Monitoring_FakeCluster>(Cluster)
            .WithHttpEndpoint(name: "api")
            .WithEnvironment("FAKECLUSTER_PODS", string.Join(',', Applications.Concat(Chain.Keys).Select(application => $"{application}-1:{application}")));

        // What the chain depends on, as containers: a broker and a database.
        var broker = builder.AddContainer("broker", "apache/activemq-artemis", "latest-alpine")
            .WithEnvironment("ARTEMIS_USER", "art")
            .WithEnvironment("ARTEMIS_PASSWORD", "e2e-broker")
            .WithEndpoint(name: "amqp", targetPort: 5672, scheme: "tcp");
        var database = builder.AddContainer("database", "postgres", "17-alpine")
            .WithEnvironment("POSTGRES_PASSWORD", "e2e-database")
            .WithEndpoint(name: "sql", targetPort: 5432, scheme: "tcp");
        var brokerEndpoint = broker.GetEndpoint("amqp");
        var databaseEndpoint = database.GetEndpoint("sql");

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
            .WithEnvironment("MONITORING_Controller__Alerting__ScopeUnknownWait", "00:00:10")
            .WithEnvironment("MONITORING_Controller__Web__Auth__Tokens__0__Name", "e2e")
            .WithEnvironment("MONITORING_Controller__Web__Auth__Tokens__0__Hash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DashboardToken))))
            .WithEnvironment("MONITORING_Controller__Notifications__Interval", "00:00:01")
            .WithEnvironment("MONITORING_Controller__Notifications__Webhooks__0__Url", ReferenceExpression.Create($"{cluster.GetEndpoint("api").Property(EndpointProperty.Url)}/receive/webhook"))
            .WithEnvironment("MONITORING_Controller__Notifications__Webhooks__0__Secret", WebhookSecret)
            .WithEnvironment("MONITORING_Controller__Configuration__File", CentralConfiguration(runId))
            .WithEnvironment("MONITORING_Controller__Configuration__PollInterval", "00:00:01")
            .WithHttpEndpoint(name: "metrics", env: "MONITORING_Controller__Prometheus__Port")
            .WithEnvironment("MONITORING_Controller__Prometheus__Address", "127.0.0.1")
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

        IResourceBuilder<ProjectResource>? data = null;
        foreach (var (name, role) in Chain.OrderByDescending(entry => entry.Value == "data"))
        {
            var socket = Path.Combine(state, $"{name}.sock");
            var app = builder.AddProject<Projects.Monitoring_TestApplications>(name)
                .WithHttpEndpoint(name: "http")
                .WithEnvironment("ROLE", role)
                .WithEnvironment("SERVICE_NAME", name)
                .WithEnvironment("MONITORING_SOCKET", socket)
                .WithEnvironment("BROKEN_FILE", BrokenSwitch(runId, name))
                .WithEnvironment("ARTEMIS", ReferenceExpression.Create($"{brokerEndpoint.Property(EndpointProperty.IPV4Host)}:{brokerEndpoint.Property(EndpointProperty.Port)}:art:e2e-broker"))
                .WithEnvironment("POSTGRES", ReferenceExpression.Create($"Host={databaseEndpoint.Property(EndpointProperty.IPV4Host)};Port={databaseEndpoint.Property(EndpointProperty.Port)};Username=postgres;Password=e2e-database"));
            if (role == "data")
            {
                data = app;
            }
            else if (role == "worker")
            {
                app.WithEnvironment("DATA_URL", data!.GetEndpoint("http"));
            }

            builder.AddProject<Projects.Monitoring_Sidecar>($"{name}-sidecar")
                .WithEnvironment("MONITORING_Sidecar__Agent", ReferenceExpression.Create($"{sidecarsEndpoint.Property(EndpointProperty.IPV4Host)}:{sidecarsEndpoint.Property(EndpointProperty.Port)}"))
                .WithEnvironment("MONITORING_Sidecar__Library__Socket", socket)
                .WithEnvironment("MONITORING_Sidecar__InstanceId", $"{name}-1")
                .WaitFor(agent)
                .WaitFor(app);
        }

        return builder;
    }
}
