using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Dashboard;

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
        json.RootElement.GetProperty("alerts").GetArrayLength().ShouldBe(0);
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
    public async Task Page_is_the_monitoring_page()
    {
        // Arrange
        await using var server = await new ApiFixture().StartAsync();

        // Act
        var html = await server.Client.GetStringAsync("/", Ct);

        // Assert
        html.ShouldContain("<title>Monitoring</title>");
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
    private sealed class ApiFixture
    {
        private readonly FakeScopeRegistry _registry = new();

        public ApiFixture WithInstance(string scope, string service, InstanceLifecycle lifecycle, HealthState health, string? id = null)
        {
            _registry.WithInstance(scope, service, lifecycle, health, id);
            return this;
        }

        public async Task<RunningApi> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<Monitoring.Controller.Features.Registry.IScopeRegistry>(_registry);
            builder.Services.AddSingleton<IHealthRollup, HealthRollup>();
            builder.Services.AddSingleton<IAlertState>(new NoAlerts());
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
}
