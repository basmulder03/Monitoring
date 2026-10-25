using System.Text.Json;
using Plover.AppHost;
using Plover.Specifications.Support;
using Aspire.Hosting.Testing;
using Reqnroll;
using Shouldly;

namespace Plover.Specifications.Steps;

/// <summary>Looking closer: containers, packages, history, the system view, the metrics export and the central configuration.</summary>
[Binding]
public sealed class InspectionSteps(DashboardDriver dashboard)
{
    [Then(@"the instance ""(.*)"" of ""(.*)"" in the scope ""(.*)"" shows a container ""(.*)"" with the role (App|PloverSidecar) within (\d+) seconds")]
    public Task ThenTheInstanceShowsAContainer(string instance, string service, string scope, string container, string role, int seconds) =>
        dashboard.WaitUntilAsync($"'{instance}' shows the container '{container}' as {role}", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            return document!.RootElement.GetProperty("services").EnumerateArray().Where(s => s.GetProperty("name").GetString() == service)
                .SelectMany(s => s.GetProperty("instances").EnumerateArray()).Where(i => i.GetProperty("id").GetString() == instance)
                .SelectMany(i => i.GetProperty("containers").EnumerateArray())
                .Any(c => c.GetProperty("name").GetString() == container && c.GetProperty("role").GetString() == role);
        }, $"/api/scopes/{scope}");

    [Then(@"the instance ""(.*)"" of ""(.*)"" in the scope ""(.*)"" shows that the container ""(.*)"" waits with ""(.*)"" after ending with ""(.*)"" within (\d+) seconds")]
    public Task ThenTheContainerIsStuck(string instance, string service, string scope, string container, string reason, string lastReason, int seconds) =>
        dashboard.WaitUntilAsync($"'{container}' of '{instance}' waits with {reason} after {lastReason}", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            return document!.RootElement.GetProperty("services").EnumerateArray().Where(s => s.GetProperty("name").GetString() == service)
                .SelectMany(s => s.GetProperty("instances").EnumerateArray()).Where(i => i.GetProperty("id").GetString() == instance)
                .SelectMany(i => i.GetProperty("containers").EnumerateArray())
                .Any(c => c.GetProperty("name").GetString() == container && c.GetProperty("reason").GetString() == reason && c.GetProperty("lastReason").GetString() == lastReason);
        }, $"/api/scopes/{scope}");

    [Then(@"the packages of the applications include ""(.*)"" within (\d+) seconds")]
    public Task ThenThePackagesInclude(string package, int seconds) =>
        dashboard.WaitUntilAsync($"the package '{package}' is known", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.GetAsync("/api/packages");
            return document!.RootElement.GetProperty("packages").EnumerateArray().Any(p => p.GetProperty("name").GetString() == package);
        }, "/api/packages");

    [Then(@"the metric history of the instance ""(.*)"" in the scope ""(.*)"" has samples within (\d+) seconds")]
    public Task ThenTheHistoryHasSamples(string instance, string scope, int seconds) =>
        dashboard.WaitUntilAsync($"'{instance}' has metric history", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.GetAsync($"/api/scopes/{scope}/instances/{instance}/history");
            return document is not null && document.RootElement.GetProperty("series").GetArrayLength() > 0;
        }, $"/api/scopes/{scope}/instances/{instance}/history");

    [Then(@"the system view shows (\d+) of (\d+) agents connected within (\d+) seconds")]
    public Task ThenTheSystemViewShowsAgents(int connected, int total, int seconds) =>
        dashboard.WaitUntilAsync($"{connected} of {total} agents are connected", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.GetAsync("/api/system");
            return document is not null && document.RootElement.GetProperty("agentsConnected").GetInt32() == connected && document.RootElement.GetProperty("scopes").GetInt32() == total;
        }, "/api/system");

    [Then(@"the system view lists no pod without a Plover sidecar within (\d+) seconds")]
    public Task ThenNoPodWithoutSidecar(int seconds) =>
        dashboard.WaitUntilAsync("no pod is listed without a sidecar", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.GetAsync("/api/system");
            return document is not null && document.RootElement.GetProperty("unmonitored").GetArrayLength() == 0;
        }, "/api/system");

    [Then(@"the metrics export of the controller says ""(.*)"" within (\d+) seconds")]
    public Task ThenTheExportSays(string line, int seconds) =>
        dashboard.WaitUntilAsync($"the metrics export has '{line}'", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var client = RunningEnvironment.App.CreateHttpClient(PloverEnvironment.Controller, "metrics");
            client.Timeout = TimeSpan.FromSeconds(5);
            return (await client.GetStringAsync("/metrics")).Split('\n').Any(l => l.StartsWith(line, StringComparison.Ordinal));
        });

    [When(@"the central configuration sets pullSeconds to (\d+)")]
    public void WhenTheCentralConfigurationSets(int seconds) =>
        File.WriteAllText(PloverEnvironment.CentralConfiguration(RunningEnvironment.RunId), $"{{ \"defaults\": {{ \"pullSeconds\": {seconds}, \"edgeFlushSeconds\": 10 }} }}");

    [When("the central configuration is replaced by a file that is not valid")]
    public void WhenTheCentralConfigurationIsInvalid() =>
        File.WriteAllText(PloverEnvironment.CentralConfiguration(RunningEnvironment.RunId), "{ this is not json");

    [Then(@"the scope ""(.*)"" shows its agent in step with the central configuration within (\d+) seconds")]
    public Task ThenTheScopeIsInSync(string scope, int seconds) =>
        dashboard.WaitUntilAsync($"'{scope}' is in step with the configuration", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            return document!.RootElement.GetProperty("configuration").GetProperty("inSync").GetBoolean();
        }, $"/api/scopes/{scope}");

    [Then(@"the scope ""(.*)"" runs a different configuration version than before within (\d+) seconds")]
    public Task ThenTheVersionChanged(string scope, int seconds) =>
        dashboard.WaitUntilAsync($"'{scope}' runs a new configuration version", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            var configuration = document!.RootElement.GetProperty("configuration");
            return configuration.GetProperty("inSync").GetBoolean() && configuration.GetProperty("applied").GetString() != "built-in-15-10";
        }, $"/api/scopes/{scope}");

    [Then(@"the scope ""(.*)"" still runs the configuration of the valid file within (\d+) seconds")]
    public Task ThenTheValidConfigurationStays(string scope, int seconds) =>
        dashboard.WaitUntilAsync($"'{scope}' keeps the valid configuration", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            var configuration = document!.RootElement.GetProperty("configuration");
            return configuration.GetProperty("inSync").GetBoolean() && configuration.GetProperty("applied").ValueKind == JsonValueKind.String;
        }, $"/api/scopes/{scope}");

    [Then(@"a visitor without a token is refused the page ""(.*)""")]
    public async Task ThenAVisitorWithoutATokenIsRefused(string path) =>
        (await dashboard.VisitAsync(path, token: null)).Status.ShouldBe(401);

    [Then(@"a visitor with a wrong token is refused the page ""(.*)""")]
    public async Task ThenAVisitorWithAWrongTokenIsRefused(string path) =>
        (await dashboard.VisitAsync(path, token: "not-the-token")).Status.ShouldBe(401);

    [Then(@"a visitor without a token may ask the health probe ""(.*)""")]
    public async Task ThenTheProbeIsOpen(string path) =>
        (await dashboard.VisitAsync(path, token: null)).Status.ShouldBe(200);

    [Then(@"a visitor with the dashboard token is shown the page ""(.*)"" and the token is kept in a cookie")]
    public async Task ThenAVisitorWithTheTokenIsShownThePage(string path)
    {
        var (status, setsCookie) = await dashboard.VisitAsync(path, PloverEnvironment.DashboardToken);
        status.ShouldBe(200);
        setsCookie.ShouldBeTrue();
    }
}
