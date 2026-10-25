using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.AgentLink;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Configuration;
using Plover.Controller.Features.Dashboard;
using Plover.Controller.Features.Health;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Features.Topology;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Dashboard;

public class DashboardEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overview_answers_with_json()
    {
        // Arrange
        await using var server = await new ApiFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy).StartAsync();

        // Act
        using var response = await server.Client.GetAsync("/api/overview", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Topology_answers_with_nodes_and_edges_as_json()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var json = await server.GetJsonAsync("/api/topology");

        // Assert
        var node = json.RootElement.GetProperty("nodes")[0];
        node.GetProperty("id").GetString().ShouldBe("service:shop/orders");
        node.GetProperty("kind").GetString().ShouldBe("Service");
        node.GetProperty("health").GetString().ShouldBe("Healthy");
        var edge = json.RootElement.GetProperty("edges")[0];
        edge.GetProperty("from").GetString().ShouldBe("service:shop/orders");
        edge.GetProperty("to").GetString().ShouldBe("database:mssql://db1/orders");
        edge.GetProperty("calls").GetInt64().ShouldBe(7);
    }

    [Fact]
    public async Task Overview_uses_camel_case_names_and_text_for_health()
    {
        // Arrange
        await using var server = await new ApiFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Unhealthy).StartAsync();

        // Act
        using var json = await server.GetJsonAsync("/api/overview");

        // Assert
        var scope = json.RootElement.GetProperty("scopes")[0];
        scope.GetProperty("name").GetString().ShouldBe("shop");
        scope.GetProperty("health").GetString().ShouldBe("Unhealthy");
        scope.GetProperty("instances").GetInt32().ShouldBe(1);
        scope.GetProperty("agentConnected").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("alerts").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("groupBy").EnumerateArray().Select(k => k.GetString()).ShouldBe(["environment", "datacenter"]);
    }

    [Fact]
    public async Task Overview_gives_each_scope_its_labels()
    {
        // Arrange
        await using var server = await new ApiFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy).WithLabel("shop", "environment", "prod").StartAsync();

        // Act
        using var json = await server.GetJsonAsync("/api/overview");

        // Assert
        json.RootElement.GetProperty("scopes")[0].GetProperty("labels").GetProperty("environment").GetString().ShouldBe("prod");
    }

    [Fact]
    public async Task Scope_answers_with_its_services_and_instances()
    {
        // Arrange
        await using var server = await new ApiFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy, "orders-1").StartAsync();

        // Act
        using var json = await server.GetJsonAsync("/api/scopes/shop");

        // Assert
        var service = json.RootElement.GetProperty("services")[0];
        service.GetProperty("name").GetString().ShouldBe("orders");
        var instance = service.GetProperty("instances")[0];
        instance.GetProperty("id").GetString().ShouldBe("orders-1");
        instance.GetProperty("lifecycle").GetString().ShouldBe("Running");
    }

    [Fact]
    public async Task Scope_with_characters_that_need_escaping_in_its_name_can_be_read()
    {
        // Arrange
        await using var server = await new ApiFixture().WithInstance("orders platform", "orders", InstanceLifecycle.Running, HealthState.Healthy).StartAsync();

        // Act
        using var json = await server.GetJsonAsync("/api/scopes/orders%20platform");

        // Assert
        json.RootElement.GetProperty("name").GetString().ShouldBe("orders platform");
    }

    [Fact]
    public async Task Scope_that_is_not_known_answers_not_found()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var response = await server.Client.GetAsync("/api/scopes/nowhere", Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/tv", "text/html")]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    public async Task Page_files_are_served_with_their_media_type(string path, string mediaType)
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var response = await server.Client.GetAsync(path, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
    }

    [Fact]
    public async Task Page_is_the_plover_page()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        var html = await server.Client.GetStringAsync("/", Ct);

        // Assert
        html.ShouldContain("<title>Plover</title>");
        html.ShouldContain("/app.js");
    }

    [Fact]
    public async Task Page_files_only_allow_the_page_to_load_its_own_files()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var response = await server.Client.GetAsync("/", Ct);

        // Assert
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self'");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("frame-ancestors 'none'");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/nothing")]
    [InlineData("/api/nothing")]
    public async Task Other_paths_are_not_found(string path)
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var response = await server.Client.GetAsync(path, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Starts the dashboard endpoints in memory over a fake registry.</summary>
    /// <summary>Knows the history of one instance only.</summary>
    private sealed class FixedHistory : IAgentRequests
    {
        public Task<string?> GetMetricHistoryAsync(string scopeName, string instanceId, CancellationToken ct) =>
            Task.FromResult<string?>(scopeName == "shop" && instanceId == "orders-1" ? "{\"intervalSeconds\":15,\"series\":[]}" : null);
    }

    [Fact]
    public async Task History_passes_on_what_the_agent_kept_and_is_empty_when_it_kept_nothing()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        using var kept = await server.Client.GetAsync("/api/scopes/shop/instances/orders-1/history", Ct);
        using var none = await server.Client.GetAsync("/api/scopes/shop/instances/other/history", Ct);

        // Assert
        (await kept.Content.ReadAsStringAsync(Ct)).ShouldBe("{\"intervalSeconds\":15,\"series\":[]}");
        kept.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        none.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private sealed class ApiFixture
    {
        private readonly FakeScopeRegistry _registry = new();

        public ApiFixture WithInstance(string scope, string service, InstanceLifecycle lifecycle, HealthState health, string? id = null)
        {
            _registry.WithInstance(scope, service, lifecycle, health, id);
            return this;
        }

        public ApiFixture WithLabel(string scope, string key, string value)
        {
            _registry.WithLabel(scope, key, value);
            return this;
        }

        public async Task<RunningApi> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<Plover.Controller.Features.Registry.IScopeRegistry>(_registry);
            builder.Services.AddSingleton<IAgentLiveness>(new FakeAgentLiveness());
            builder.Services.AddSingleton<IHealthRollup, HealthRollup>();
            builder.Services.AddSingleton<IAlertState>(new NoAlerts());
            builder.Services.AddSingleton<ITopology>(new FixedTopology());
            builder.Services.AddSingleton<IAgentRequests>(new FixedHistory());
            builder.Services.AddSingleton<IAppliedConfigurations>(new AppliedConfigurations(new EffectiveConfiguration(Microsoft.Extensions.Logging.Abstractions.NullLogger<EffectiveConfiguration>.Instance)));
            builder.Services.AddDashboard();

            var app = builder.Build();
            app.MapDashboard();
            await app.StartAsync(Ct);
            return new RunningApi(app);
        }
    }

    /// <summary>A started in-memory dashboard API and a client for it.</summary>
    private sealed class RunningApi(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            var text = await Client.GetStringAsync(path, Ct);
            return JsonDocument.Parse(text);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync(Ct);
            await app.DisposeAsync();
        }
    }

    private sealed class NoAlerts : IAlertState
    {
        public IReadOnlyList<Alert> GetActiveAlerts() => [];
    }

    private sealed class FixedTopology : ITopology
    {
        public TopologyGraph GetGraph() => new(
            [
                new TopologyNode("service:shop/orders", "orders", NodeKind.Service, "shop", HealthState.Healthy),
                new TopologyNode("database:mssql://db1/orders", "mssql://db1/orders", NodeKind.Database, null, null),
            ],
            [new TopologyEdge("service:shop/orders", "database:mssql://db1/orders", "DB", 7, 0, 35)]);
    }
}
