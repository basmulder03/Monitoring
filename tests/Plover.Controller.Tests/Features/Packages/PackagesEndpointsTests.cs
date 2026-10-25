using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.Registry;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Packages;

public class PackagesEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Packages_answers_with_the_catalog_as_json()
    {
        // Arrange
        await using var api = await new ApiFixture().StartAsync();

        // Act
        using var json = await api.GetJsonAsync("/api/packages");

        // Assert
        var serilog = json.RootElement.GetProperty("packages")[0];
        serilog.GetProperty("name").GetString().ShouldBe("Serilog");
        serilog.GetProperty("versions")[0].GetProperty("version").GetString().ShouldBe("4.0.0");
        serilog.GetProperty("versions")[0].GetProperty("usedBy")[0].GetProperty("service").GetString().ShouldBe("orders");
        json.RootElement.GetProperty("servicesWithoutList").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task One_package_answers_by_name_and_not_found_when_unused()
    {
        // Arrange
        await using var api = await new ApiFixture().StartAsync();

        // Act
        using var found = await api.Client.GetAsync("/api/packages/serilog", Ct);
        using var missing = await api.Client.GetAsync("/api/packages/Nope", Ct);

        // Assert
        found.StatusCode.ShouldBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_landscape_sbom_is_a_cyclonedx_document()
    {
        // Arrange
        await using var api = await new ApiFixture().StartAsync();

        // Act
        using var response = await api.Client.GetAsync("/api/sbom", Ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // Assert
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.cyclonedx+json");
        json.RootElement.GetProperty("bomFormat").GetString().ShouldBe("CycloneDX");
        json.RootElement.GetProperty("components")[0].GetProperty("bom-ref").GetString().ShouldBe("pkg:nuget/Serilog@4.0.0");
        json.RootElement.GetProperty("metadata").GetProperty("timestamp").GetString().ShouldBe("2026-11-02T08:30:15Z");
    }

    [Fact]
    public async Task The_sbom_of_a_service_names_the_service_and_is_not_found_for_an_unknown_one()
    {
        // Arrange
        await using var api = await new ApiFixture().StartAsync();

        // Act
        using var json = await api.GetJsonAsync("/api/sbom/shop/orders");
        using var missing = await api.Client.GetAsync("/api/sbom/shop/nope", Ct);

        // Assert
        json.RootElement.GetProperty("metadata").GetProperty("component").GetProperty("name").GetString().ShouldBe("orders");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed class ApiFixture
    {
        public async Task<RunningApi> StartAsync()
        {
            var registry = new FakeScopeRegistry().WithInstance(
                "shop",
                new InstanceInfo(
                    "orders-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0",
                    Packages: [new PackageReport("Serilog", "4.0.0", true)]));

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IScopeRegistry>(registry);
            builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 8, 30, 15, TimeSpan.Zero)));
            builder.Services.AddPackages();

            var app = builder.Build();
            app.MapPackages();
            await app.StartAsync(Ct);
            return new RunningApi(app);
        }
    }

    private sealed class RunningApi(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public async Task<JsonDocument> GetJsonAsync(string path) => JsonDocument.Parse(await Client.GetStringAsync(path, Ct));

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync(Ct);
            await app.DisposeAsync();
        }
    }
}
