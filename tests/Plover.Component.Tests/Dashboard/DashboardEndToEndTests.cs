using System.Text.Json;
using Plover.Component.Tests.Support;
using Shouldly;
using Xunit;

namespace Plover.Component.Tests.Dashboard;

/// <summary>The dashboard of a real controller showing what a real agent discovered in a (fake) cluster.</summary>
public class DashboardEndToEndTests
{
    private const string Token = "enroll";

    [Fact]
    public async Task The_overview_shows_the_scope_the_agent_reports_for()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").WithPod("orders-2").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();

        // Act / Assert
        await Eventually.AssertAsync(() =>
        {
            using var overview = GetJson(http, $"{controller.WebAddress}/api/overview");
            var scope = overview.RootElement.GetProperty("scopes")[0];
            scope.GetProperty("name").GetString().ShouldBe("shop");
            scope.GetProperty("health").GetString().ShouldBe("Healthy");
            scope.GetProperty("instances").GetInt32().ShouldBe(2);
        });
    }

    [Fact]
    public async Task The_overview_gives_each_scope_the_labels_of_its_agent()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster)
            .With("Agent:Labels:environment", "prod").With("Agent:Labels:datacenter", "a").Start();
        using var http = Web();

        // Act / Assert
        await Eventually.AssertAsync(() =>
        {
            using var overview = GetJson(http, $"{controller.WebAddress}/api/overview");
            var labels = overview.RootElement.GetProperty("scopes")[0].GetProperty("labels");
            labels.GetProperty("environment").GetString().ShouldBe("prod");
            labels.GetProperty("datacenter").GetString().ShouldBe("a");
        });
    }

    [Fact]
    public async Task The_scope_view_lists_the_service_and_its_pods()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").WithPod("orders-2").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();

        // Act / Assert
        await Eventually.AssertAsync(() =>
        {
            using var scope = GetJson(http, $"{controller.WebAddress}/api/scopes/shop");
            var service = scope.RootElement.GetProperty("services")[0];
            service.GetProperty("name").GetString().ShouldBe("orders");
            service.GetProperty("instances").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ShouldBe(["orders-1", "orders-2"]);
        });
    }

    [Fact]
    public async Task The_live_stream_pushes_the_new_state_when_a_pod_stops_being_ready()
    {
        // Arrange
        await using var cluster = new FakeKubernetesApiServer().WithPod("orders-1").Start();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).DiscoveringIn(cluster).Start();
        using var http = Web();
        await Eventually.AssertAsync(() => cluster.OpenWatches.ShouldBe(1));
        using var response = await http.GetAsync($"{controller.WebAddress}/api/events", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        await ReadUntilAsync(reader, health: "Healthy");

        // Act
        cluster.ChangePod("orders-1", ready: false);

        // Assert
        var data = await ReadUntilAsync(reader, health: "Degraded");
        data.ShouldContain("\"name\":\"shop\"");
    }

    [Fact]
    public async Task The_page_is_served_by_the_controller()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).Start();
        using var http = Web();

        // Act
        var html = await http.GetStringAsync($"{controller.WebAddress}/", TestContext.Current.CancellationToken);

        // Assert
        html.ShouldContain("<title>Plover</title>");
    }

    /// <summary>A client that talks to the dashboard directly, without a proxy from the environment.</summary>
    private static HttpClient Web() => new(new HttpClientHandler { UseProxy = false });

    private static JsonDocument GetJson(HttpClient http, string url) =>
        JsonDocument.Parse(http.GetStringAsync(url, TestContext.Current.CancellationToken).GetAwaiter().GetResult());

    /// <summary>Reads stream events until an overview shows a scope with the given health; fails instead of hanging.</summary>
    private static async Task<string> ReadUntilAsync(StreamReader reader, string health)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(15));
        while (await reader.ReadLineAsync(guard.Token) is { } line)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && line.Contains($"\"health\":\"{health}\"", StringComparison.Ordinal))
            {
                return line["data: ".Length..];
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }
}
