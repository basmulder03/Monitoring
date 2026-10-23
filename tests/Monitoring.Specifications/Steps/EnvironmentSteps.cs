using Aspire.Hosting.Testing;
using Monitoring.AppHost;
using Monitoring.Specifications.Support;
using Reqnroll;
using Shouldly;

namespace Monitoring.Specifications.Steps;

[Binding]
public sealed class EnvironmentSteps
{
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(0);

    [Given("the environment is running")]
    public void GivenTheEnvironmentIsRunning() => RunningEnvironment.App.ShouldNotBeNull();

    [Given(@"the dashboard shows the service ""(.*)"" in the scope ""(.*)"" as (Healthy|Unhealthy|Degraded|Unknown) within (\d+) seconds")]
    [Then(@"the dashboard shows the service ""(.*)"" in the scope ""(.*)"" as (Healthy|Unhealthy|Degraded|Unknown) within (\d+) seconds")]
    public async Task ThenTheDashboardShowsTheService(string service, string scope, string health, int seconds)
    {
        try
        {
            await ShowsTheService(service, scope, health, seconds);
        }
        catch (TimeoutException ex)
        {
            using var document = await RunningEnvironment.GetJsonAsync($"/api/scopes/{scope}");
            throw new TimeoutException($"{ex.Message}. The dashboard shows: {document?.RootElement.GetRawText() ?? "nothing"}");
        }
    }

    private static Task ShowsTheService(string service, string scope, string health, int seconds) =>
        Eventually.HoldsAsync($"the service '{service}' in '{scope}' is {health}", TimeSpan.FromSeconds(seconds) + Slack, async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync($"/api/scopes/{scope}");
            return document is not null && document.RootElement.GetProperty("services").EnumerateArray()
                .Any(s => s.GetProperty("name").GetString() == service && s.GetProperty("health").GetString() == health);
        });

    [When(@"the application ""(.*)"" breaks")]
    public void WhenTheApplicationBreaks(string application) =>
        File.WriteAllText(MonitoringEnvironment.BrokenSwitch(RunningEnvironment.RunId, application), "broken");

    [When(@"the application ""(.*)"" is repaired")]
    public void WhenTheApplicationIsRepaired(string application) =>
        File.Delete(MonitoringEnvironment.BrokenSwitch(RunningEnvironment.RunId, application));

    [When(@"the application ""(.*)"" calls the application ""(.*)"" (\d+) times")]
    public async Task WhenTheApplicationCallsTheApplication(string caller, string callee, int times)
    {
        var target = RunningEnvironment.App.GetEndpoint(callee, "http");
        using var client = RunningEnvironment.App.CreateHttpClient(caller, "http");
        for (var call = 0; call < times; call++)
        {
            using var response = await client.GetAsync($"/call?to={Uri.EscapeDataString(target.ToString())}");
            response.EnsureSuccessStatusCode();
        }
    }

    [Then(@"the dependency graph has a call from ""(.*)"" to the address of ""(.*)"" within (\d+) seconds")]
    public Task ThenTheDependencyGraphHasACall(string caller, string callee, int seconds)
    {
        var port = RunningEnvironment.App.GetEndpoint(callee, "http").Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Eventually.HoldsAsync($"the graph has a call from '{caller}' to port {port}", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync("/api/topology");
            return document is not null && document.RootElement.GetProperty("edges").EnumerateArray()
                .Any(e => e.GetProperty("from").GetString()!.EndsWith($"/{caller}", StringComparison.Ordinal)
                    && e.GetProperty("to").GetString()!.Contains(port, StringComparison.Ordinal)
                    && e.GetProperty("calls").GetInt64() > 0);
        });
    }

    [Then(@"the dashboard shows the alert ""(.*)"" for ""(.*)"" within (\d+) seconds")]
    public Task ThenTheDashboardShowsTheAlert(string rule, string subjectStart, int seconds) =>
        Eventually.HoldsAsync($"the alert '{rule}' for '{subjectStart}' is shown", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync("/api/overview");
            return document is not null && document.RootElement.GetProperty("alerts").EnumerateArray()
                .Any(a => a.GetProperty("rule").GetString() == rule && a.GetProperty("subject").GetString()!.StartsWith(subjectStart, StringComparison.Ordinal));
        });

    [Then(@"the dashboard shows no alert ""(.*)"" within (\d+) seconds")]
    public Task ThenTheDashboardShowsNoAlert(string rule, int seconds) =>
        Eventually.HoldsAsync($"no alert '{rule}' is shown", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync("/api/overview");
            return document is not null && document.RootElement.GetProperty("alerts").EnumerateArray().All(a => a.GetProperty("rule").GetString() != rule);
        });

    [When(@"the application ""(.*)"" is asked for an order (\d+) times")]
    public async Task WhenTheApplicationIsAskedForAnOrder(string application, int times)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(application, "http");
        for (var order = 0; order < times; order++)
        {
            using var response = await client.GetAsync("/order");
            response.EnsureSuccessStatusCode();
        }
    }

    [Then(@"the dependency graph has a ""(.*)"" call from ""(.*)"" to the queue ""(.*)"" within (\d+) seconds")]
    public Task ThenTheGraphHasACallToTheQueue(string type, string caller, string queue, int seconds) =>
        GraphHasAsync($"a {type} call from '{caller}' to the queue '{queue}'", seconds, edge =>
            edge.Type == type && edge.From.EndsWith($"/{caller}", StringComparison.Ordinal) && edge.To.StartsWith("queue:", StringComparison.Ordinal) && edge.To.EndsWith($"//{queue}", StringComparison.Ordinal));

    [Then(@"the dependency graph has a ""(.*)"" call from the queue ""(.*)"" to ""(.*)"" within (\d+) seconds")]
    public Task ThenTheGraphHasACallFromTheQueue(string type, string queue, string callee, int seconds) =>
        GraphHasAsync($"a {type} call from the queue '{queue}' to '{callee}'", seconds, edge =>
            edge.Type == type && edge.To.EndsWith($"/{callee}", StringComparison.Ordinal) && edge.From.StartsWith("queue:", StringComparison.Ordinal) && edge.From.EndsWith($"//{queue}", StringComparison.Ordinal));

    [Then(@"the dependency graph has a ""(.*)"" call from ""(.*)"" to a database within (\d+) seconds")]
    public Task ThenTheGraphHasACallToADatabase(string type, string caller, int seconds) =>
        GraphHasAsync($"a {type} call from '{caller}' to a database", seconds, edge =>
            edge.Type == type && edge.From.EndsWith($"/{caller}", StringComparison.Ordinal) && edge.To.StartsWith("database:", StringComparison.Ordinal));

    private sealed record Edge(string From, string To, string Type);

    private static async Task GraphHasAsync(string what, int seconds, Func<Edge, bool> matches)
    {
        try
        {
            await Eventually.HoldsAsync(what, TimeSpan.FromSeconds(seconds), async () =>
            {
                using var document = await RunningEnvironment.GetJsonAsync("/api/topology");
                return document is not null && document.RootElement.GetProperty("edges").EnumerateArray()
                    .Select(e => new Edge(e.GetProperty("from").GetString()!, e.GetProperty("to").GetString()!, e.GetProperty("type").GetString()!))
                    .Any(matches);
            });
        }
        catch (TimeoutException ex)
        {
            using var document = await RunningEnvironment.GetJsonAsync("/api/topology");
            throw new TimeoutException($"{ex.Message}. The graph is: {document?.RootElement.GetRawText() ?? "nothing"}");
        }
    }
}
