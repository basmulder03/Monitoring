using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Plover.Component.Tests.Support;
using Shouldly;
using Xunit;

namespace Plover.Component.Tests.Browser;

/// <summary>The dashboard in a real browser, fed by real agents and a real controller from a fake cluster.</summary>
[Collection(BrowserCollection.Name)]
public class BrowserDashboardTests(BrowserFixture browser)
{
    private const string Token = "enroll";

    private static readonly LocatorAssertionsToBeVisibleOptions Patient = new() { Timeout = 15000 };

    private static ILocator Tiles(IPage page) => page.Locator("a.tile");

    private static async Task ExpectAsync(ILocator locator, string text) =>
        await Assertions.Expect(locator).ToContainTextAsync(text, new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

    [Fact]
    public async Task An_empty_dashboard_says_that_no_scope_has_reported_yet()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync(world.Url);

        // Assert
        await ExpectAsync(page.Page.Locator("main"), "No scope has reported yet.");
        await Assertions.Expect(page.Page).ToHaveTitleAsync("Plover");
        page.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scopes_are_grouped_by_environment_with_the_least_healthy_group_first()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld()
            .WithScope("billing", "test", "b", ("invoices-1", true))
            .WithScope("shop", "prod", "a", ("orders-1", true), ("orders-2", false))
            .StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync(world.Url);
        await Assertions.Expect(Tiles(page.Page)).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await ExpectAsync(Tiles(page.Page).Filter(new LocatorFilterOptions { HasText = "shop" }), "Degraded");

        // Assert: the prod group holds a degraded scope, so it comes before the healthy test group
        var text = await page.Page.Locator("main").InnerTextAsync();
        text.IndexOf("environment: prod", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("environment: test", StringComparison.Ordinal));
        text.ShouldContain("datacenter: a");
        page.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_status_is_shown_as_an_icon_and_a_word()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync(world.Url);
        var status = page.Page.Locator("a.tile .status").First;

        // Assert
        await Assertions.Expect(status).ToContainTextAsync("Healthy", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(status.Locator("svg")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Selecting_a_tile_shows_its_services_and_pods_and_the_link_back_returns_to_the_overview()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true), ("orders-2", true)).StartAsync();
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), "Healthy");

        // Act
        await Tiles(page.Page).First.ClickAsync();

        // Assert
        await Assertions.Expect(page.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("#/scopes/shop$"));
        await ExpectAsync(page.Page.Locator("main"), "orders");
        await ExpectAsync(page.Page.Locator("main"), "orders-1");
        await ExpectAsync(page.Page.Locator("main"), "orders-2");
        await page.Page.GetByText("← All scopes").ClickAsync();
        await Assertions.Expect(Tiles(page.Page)).ToHaveCountAsync(1);
        page.Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pod_that_stops_being_ready_changes_the_tile_without_reloading_and_is_announced()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), "Healthy");
        await world.WaitUntilWatchingAsync("shop");

        // Act
        world.Cluster("shop").ChangePod("orders-1", ready: false);

        // Assert
        await ExpectAsync(Tiles(page.Page), "Degraded");
        await ExpectAsync(page.Page.Locator("#announcer"), "shop is now Degraded");
    }

    [Fact]
    public async Task A_scope_whose_agent_is_gone_turns_unknown_and_says_so()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), "Healthy");

        // Act
        await world.StopAgentAsync("shop");

        // Assert
        await ExpectAsync(Tiles(page.Page), "Unknown");
        await ExpectAsync(Tiles(page.Page), "Agent not connected");
        await ExpectAsync(page.Page.Locator(".alert"), "scope-unknown");
    }

    [Fact]
    public async Task The_page_warns_when_the_controller_stops_answering()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), "Healthy");

        // Act
        await world.StopControllerAsync();

        // Assert: a frozen screen must never show stale green without saying so
        await Assertions.Expect(page.Page.Locator("#stale")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await ExpectAsync(page.Page.Locator("#stale"), "seconds old");
        await world.DisposeAsync();
    }

    [Fact]
    public async Task The_filter_in_the_address_narrows_the_scopes_and_the_selector_switches_between_environments()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld()
            .WithScope("billing", "test", "b", ("invoices-1", true))
            .WithScope("shop", "prod", "a", ("orders-1", true))
            .StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync($"{world.Url}/?filter=environment:prod");
        await ExpectAsync(Tiles(page.Page), "shop");

        // Assert
        await Assertions.Expect(Tiles(page.Page)).ToHaveCountAsync(1);
        await page.Page.Locator("#filter-select").SelectOptionAsync("test");
        await ExpectAsync(Tiles(page.Page), "billing");
        await Assertions.Expect(Tiles(page.Page)).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task The_dark_theme_can_be_forced_and_switched_with_the_button()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync($"{world.Url}/?theme=dark");
        var dark = await page.Page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        await page.Page.Locator("#theme-toggle").ClickAsync();
        var light = await page.Page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");

        // Assert
        dark.ShouldBe("rgb(20, 18, 15)");
        light.ShouldBe("rgb(251, 249, 245)");
    }

    [Fact]
    public async Task The_wall_display_page_uses_large_type_and_hides_the_cursor()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync($"{world.Url}/tv");
        var cursor = await page.Page.EvaluateAsync<string>("getComputedStyle(document.body).cursor");
        var size = await page.Page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.body).fontSize)");

        // Assert
        cursor.ShouldBe("none");
        size.ShouldBeGreaterThan(16);
    }

    [Fact]
    public async Task A_tile_can_be_reached_and_opened_with_the_keyboard_and_shows_a_focus_ring()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld().WithScope("shop", "prod", "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), "Healthy");

        // Act
        for (var presses = 0; presses < 12 && !await page.Page.EvaluateAsync<bool>("document.activeElement.classList.contains('tile')"); presses++)
        {
            await page.Page.Keyboard.PressAsync("Tab");
        }

        var outlineWidth = await page.Page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineWidth");
        var outlineStyle = await page.Page.EvaluateAsync<string>("getComputedStyle(document.activeElement).outlineStyle");
        await page.Page.Keyboard.PressAsync("Enter");

        // Assert
        outlineWidth.ShouldBe("3px");
        outlineStyle.ShouldBe("solid");
        await Assertions.Expect(page.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("#/scopes/shop$"));
    }

    [Fact]
    public async Task Names_and_labels_from_agents_are_shown_as_text_and_never_run()
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        const string evil = "<img src=x onerror=window.__pwned=1>";
        await using var world = await new DashboardWorld().WithScope(evil, evil, "a", ("orders-1", true)).StartAsync();
        await using var page = await browser.NewPageAsync();

        // Act
        await page.Page.GotoAsync(world.Url);
        await ExpectAsync(Tiles(page.Page), evil);
        await page.Page.WaitForTimeoutAsync(500);

        // Assert
        (await page.Page.EvaluateAsync<bool>("window.__pwned === undefined")).ShouldBeTrue();
        (await page.Page.Locator("main img").CountAsync()).ShouldBe(0);
        await ExpectAsync(page.Page.Locator("main"), $"environment: {evil}");
    }

    public static TheoryData<string, string, string> AccessibilityCases() => new()
    {
        { "overview in the light theme", "/?theme=light", "a.tile" },
        { "overview in the dark theme", "/?theme=dark", "a.tile" },
        { "wall display in the dark theme", "/tv?theme=dark", "a.tile" },
        { "scope view in the light theme", "/?theme=light#/scopes/shop", "details.service" },
        { "scope view in the dark theme", "/?theme=dark#/scopes/shop", "details.service" },
        { "scope view of a scope without its agent", "/?theme=light#/scopes/search", "main .banner" },
        { "system view in the light theme", "/?theme=light#/system", "main table.instances" },
        { "system view in the dark theme", "/?theme=dark#/system", "main table.instances" },
        { "packages view in the light theme", "/?theme=light#/packages", "main h2" },
        { "packages view in the dark theme", "/?theme=dark#/packages", "main h2" },
        { "dependencies view in the light theme", "/?theme=light#/dependencies", "main h2" },
        { "dependencies view in the dark theme", "/?theme=dark#/dependencies", "main h2" },
    };

    [Theory]
    [InlineData("/", "a.tile")]
    [InlineData("/#/scopes/shop", "details.service")]
    [InlineData("/#/system", "main table.instances")]
    [InlineData("/#/packages", "main h2")]
    [InlineData("/#/dependencies", "main h2")]
    public async Task On_a_phone_nothing_makes_the_page_wider_than_the_screen(string path, string readyWhenVisible)
    {
        // Arrange
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld()
            .WithScope("shop", "prod", "a", ("orders-1", true), ("orders-2", false))
            .WithScope("billing", "test", "b", ("invoices-1", true))
            .StartAsync();
        await world.WaitUntilScopesAreKnownAsync(2);
        await using var page = await browser.NewPageAsync(width: 390);

        // Act
        await page.Page.GotoAsync(world.Url + path);
        await Assertions.Expect(page.Page.Locator(readyWhenVisible).First).ToBeVisibleAsync(Patient);
        var (scroll, client) = await page.Page.EvaluateAsync<int[]>("[document.documentElement.scrollWidth, document.documentElement.clientWidth]") is [var a, var b] ? (a, b) : (0, 0);

        // Assert
        scroll.ShouldBeLessThanOrEqualTo(client, $"{path} is {scroll} px wide on a screen of {client} px");
    }

    [Theory]
    [MemberData(nameof(AccessibilityCases))]
    public async Task The_page_has_no_accessibility_violations_at_wcag_2_aa(string view, string path, string readyWhenVisible)
    {
        // Arrange: a busy dashboard with healthy, degraded and unknown scopes, grouping, an alert and a disconnected agent
        Assert.SkipUnless(browser.IsAvailable, browser.Reason);
        await using var world = await new DashboardWorld()
            .WithScope("shop", "prod", "a", ("orders-1", true), ("orders-2", false))
            .WithScope("billing", "test", "b", ("invoices-1", true))
            .WithScope("search", "test", "b", ("index-1", true))
            .StartAsync();
        await world.WaitUntilScopesAreKnownAsync(3);
        await world.StopAgentAsync("search");
        await world.WaitUntilAlertFiresAsync("scope-unknown");
        await using var page = await browser.NewPageAsync();
        await page.Page.GotoAsync(world.Url + path);
        await Assertions.Expect(page.Page.Locator(readyWhenVisible).First).ToBeVisibleAsync(Patient);

        // Act
        var result = await page.Page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa"] },
        });

        // Assert
        var violations = string.Join("; ", result.Violations.Select(v => $"{v.Id}: {v.Help} ({v.Nodes.Length} nodes)"));
        result.Violations.ShouldBeEmpty($"{view}: {violations}");
        page.Problems.ShouldBeEmpty();
    }

    /// <summary>A controller with fast timings and one real agent per scope, each discovering pods in its own fake cluster.</summary>
    private sealed class DashboardWorld : IAsyncDisposable
    {
        private readonly List<ScopeSetup> _setups = [];
        private readonly Dictionary<string, FakeKubernetesApiServer> _clusters = [];
        private readonly Dictionary<string, AgentHost> _agents = [];
        private ControllerHost? _controller;
        private bool _controllerStopped;

        public string Url => _controller!.WebAddress;

        public DashboardWorld WithScope(string scope, string environment, string datacenter, params (string Pod, bool Ready)[] pods)
        {
            _setups.Add(new ScopeSetup(scope, environment, datacenter, pods));
            return this;
        }

        public async Task<DashboardWorld> StartAsync()
        {
            _controller = new ControllerHost()
                .AcceptingEnrollment(Token)
                .With("Controller:Liveness:HeartbeatInterval", "00:00:00.200")
                .With("Controller:Alerting:StartupGrace", "00:00:00")
                .With("Controller:Alerting:ScopeUnknownWait", "00:00:01")
                .With("Controller:Alerting:EvaluationInterval", "00:00:00.200")
                .With("Controller:Dashboard:PushInterval", "00:00:00.200")
                .With("Controller:Dashboard:KeepAliveInterval", "00:00:01")
                .Start();

            foreach (var setup in _setups)
            {
                var cluster = new FakeKubernetesApiServer();
                foreach (var (pod, ready) in setup.Pods)
                {
                    cluster.WithPod(pod, ready);
                }

                cluster.Start();
                _clusters[setup.Scope] = cluster;
                _agents[setup.Scope] = new AgentHost(setup.Scope, Token)
                    .ConnectingTo(_controller)
                    .DiscoveringIn(cluster)
                    .With("Agent:Labels:environment", setup.Environment)
                    .With("Agent:Labels:datacenter", setup.Datacenter)
                    .Start();
            }

            await Task.CompletedTask;
            return this;
        }

        public FakeKubernetesApiServer Cluster(string scope) => _clusters[scope];

        public async Task WaitUntilWatchingAsync(string scope) =>
            await Eventually.AssertAsync(() => _clusters[scope].OpenWatches.ShouldBe(1));

        /// <summary>Waits until the controller knows this many scopes, asking its API like a page would.</summary>
        public async Task WaitUntilScopesAreKnownAsync(int count) =>
            await Eventually.AssertAsync(() => Overview().GetProperty("scopes").GetArrayLength().ShouldBe(count));

        /// <summary>Waits until an alert of the rule is firing.</summary>
        public async Task WaitUntilAlertFiresAsync(string rule) =>
            await Eventually.AssertAsync(() =>
                Overview().GetProperty("alerts").EnumerateArray().Any(a => a.GetProperty("rule").GetString() == rule).ShouldBeTrue());

        private System.Text.Json.JsonElement Overview()
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            var json = http.GetStringAsync($"{Url}/api/overview", TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        }

        public async Task StopAgentAsync(string scope)
        {
            await _agents[scope].DisposeAsync();
            _agents.Remove(scope);
        }

        public async Task StopControllerAsync()
        {
            _controllerStopped = true;
            await _controller!.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var agent in _agents.Values)
            {
                await agent.DisposeAsync();
            }

            foreach (var cluster in _clusters.Values)
            {
                await cluster.DisposeAsync();
            }

            if (!_controllerStopped && _controller is not null)
            {
                await _controller.DisposeAsync();
            }
        }

        private sealed record ScopeSetup(string Scope, string Environment, string Datacenter, (string Pod, bool Ready)[] Pods);
    }
}
