using Monitoring.Specifications.Support;
using Reqnroll;
using Shouldly;

namespace Monitoring.Specifications.Steps;

/// <summary>What the dashboard shows: services, alerts and the dependency graph.</summary>
[Binding]
public sealed class DashboardSteps(DashboardDriver dashboard)
{
    [Given("the environment is running")]
    public void GivenTheEnvironmentIsRunning() => RunningEnvironment.App.ShouldNotBeNull();

    [Given(@"the dashboard shows the service ""(.*)"" in the scope ""(.*)"" as (Healthy|Unhealthy|Degraded|Unknown) within (\d+) seconds")]
    [Then(@"the dashboard shows the service ""(.*)"" in the scope ""(.*)"" as (Healthy|Unhealthy|Degraded|Unknown) within (\d+) seconds")]
    public Task ThenTheServiceIs(string service, string scope, string health, int seconds) =>
        dashboard.WaitUntilAsync($"the service '{service}' in '{scope}' is {health}", TimeSpan.FromSeconds(seconds),
            async () => (await dashboard.ServiceAsync(scope, service))?.Health == health, $"/api/scopes/{scope}");

    [Then(@"the dashboard shows (\d+) instances? of the service ""(.*)"" in the scope ""(.*)"" within (\d+) seconds")]
    public Task ThenTheServiceHasInstances(int count, string service, string scope, int seconds) =>
        dashboard.WaitUntilAsync($"the service '{service}' in '{scope}' has {count} live instance(s)", TimeSpan.FromSeconds(seconds),
            async () => ((await dashboard.ServiceAsync(scope, service))?.LiveInstances ?? 0) == count, $"/api/scopes/{scope}");

    [Then(@"the dashboard shows that no agent is connected for the scope ""(.*)"" within (\d+) seconds")]
    public Task ThenNoAgentIsConnected(string scope, int seconds) =>
        dashboard.WaitUntilAsync($"no agent is connected for '{scope}'", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await dashboard.ScopeAsync(scope);
            return document is not null && !document.RootElement.GetProperty("agentConnected").GetBoolean();
        }, $"/api/scopes/{scope}");

    [Then(@"the dashboard shows the (critical|warning) alert ""(.*)"" for ""(.*)"" within (\d+) seconds")]
    public Task ThenTheAlertWithSeverityIsShown(string severity, string rule, string subjectStart, int seconds) =>
        WaitForAlertAsync(severity, rule, subjectStart, seconds);

    [Then(@"the dashboard shows the alert ""(.*)"" for ""(.*)"" within (\d+) seconds")]
    public Task ThenTheAlertIsShown(string rule, string subjectStart, int seconds) => WaitForAlertAsync("", rule, subjectStart, seconds);

    private Task WaitForAlertAsync(string severity, string rule, string subjectStart, int seconds) =>
        dashboard.WaitUntilAsync($"the {severity} alert '{rule}' for '{subjectStart}' is shown", TimeSpan.FromSeconds(seconds), async () =>
            (await dashboard.AlertsAsync()).Any(a => a.Rule == rule && a.Subject.StartsWith(subjectStart, StringComparison.Ordinal)
                && (severity.Length == 0 || a.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase))));

    [Then(@"the dashboard shows no alert ""(.*)"" within (\d+) seconds")]
    public Task ThenNoAlertIsShown(string rule, int seconds) =>
        dashboard.WaitUntilAsync($"no alert '{rule}' is shown", TimeSpan.FromSeconds(seconds), async () => (await dashboard.AlertsAsync()).All(a => a.Rule != rule));

    [Then(@"the dashboard shows no alerts within (\d+) seconds")]
    public Task ThenNoAlertsAreShown(int seconds) =>
        dashboard.WaitUntilAsync("no alerts are shown", TimeSpan.FromSeconds(seconds), async () => (await dashboard.AlertsAsync()).Count == 0);

    [Then(@"the dashboard shows exactly one alert ""(.*)"" for ""(.*)"" for the next (\d+) seconds")]
    public async Task ThenExactlyOneAlert(string rule, string subjectStart, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            (await dashboard.AlertsAsync()).Count(a => a.Rule == rule && a.Subject.StartsWith(subjectStart, StringComparison.Ordinal)).ShouldBeLessThanOrEqualTo(1);
            await Task.Delay(500);
        }
    }

    [Then(@"the dependency graph has a call from ""(.*)"" to the address of ""(.*)"" within (\d+) seconds")]
    public Task ThenTheGraphHasACallToTheAddress(string caller, string callee, int seconds)
    {
        var port = ApplicationDriver.PortOf(callee).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return GraphHasAsync($"a call from '{caller}' to port {port}", seconds, e => e.From.EndsWith($"/{caller}", StringComparison.Ordinal) && e.To.Contains(port, StringComparison.Ordinal) && e.Calls > 0);
    }

    [Then(@"the dependency graph has a ""(.*)"" call from ""(.*)"" to the queue ""(.*)"" within (\d+) seconds")]
    public Task ThenTheGraphHasACallToTheQueue(string type, string caller, string queue, int seconds) =>
        GraphHasAsync($"a {type} call from '{caller}' to the queue '{queue}'", seconds, e =>
            e.Type == type && e.From.EndsWith($"/{caller}", StringComparison.Ordinal) && e.To.StartsWith("queue:", StringComparison.Ordinal) && e.To.EndsWith($"//{queue}", StringComparison.Ordinal));

    [Then(@"the dependency graph has a ""(.*)"" call from the queue ""(.*)"" to ""(.*)"" within (\d+) seconds")]
    public Task ThenTheGraphHasACallFromTheQueue(string type, string queue, string callee, int seconds) =>
        GraphHasAsync($"a {type} call from the queue '{queue}' to '{callee}'", seconds, e =>
            e.Type == type && e.To.EndsWith($"/{callee}", StringComparison.Ordinal) && e.From.StartsWith("queue:", StringComparison.Ordinal) && e.From.EndsWith($"//{queue}", StringComparison.Ordinal));

    [Then(@"the dependency graph has a ""(.*)"" call from ""(.*)"" to a database within (\d+) seconds")]
    public Task ThenTheGraphHasACallToADatabase(string type, string caller, int seconds) =>
        GraphHasAsync($"a {type} call from '{caller}' to a database", seconds, e =>
            e.Type == type && e.From.EndsWith($"/{caller}", StringComparison.Ordinal) && e.To.StartsWith("database:", StringComparison.Ordinal));

    private Task GraphHasAsync(string what, int seconds, Func<GraphEdge, bool> matches) =>
        dashboard.WaitUntilAsync(what, TimeSpan.FromSeconds(seconds), async () => (await dashboard.EdgesAsync()).Any(matches), "/api/topology");
}
