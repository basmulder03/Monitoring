using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.AppHost;
using Monitoring.Specifications.Support;
using Reqnroll;

namespace Monitoring.Specifications.Steps;

[Binding]
public sealed class RecoverySteps
{
    [When(@"the pod ""(.*)"" of ""(.*)"" is deleted")]
    public async Task WhenThePodIsDeleted(string pod, string service)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        using var response = await client.DeleteAsync($"/control/pods/{pod}");
        response.EnsureSuccessStatusCode();
    }

    [When(@"the pod ""(.*)"" of ""(.*)"" is created")]
    public async Task WhenThePodIsCreated(string pod, string service)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        using var response = await client.PutAsync($"/control/pods/{pod}?service={service}&ready=true", content: null);
        response.EnsureSuccessStatusCode();
    }

    [Then(@"the dashboard shows no live instance of the service ""(.*)"" in the scope ""(.*)"" within (\d+) seconds")]
    public Task ThenTheDashboardShowsNoLiveInstance(string service, string scope, int seconds) =>
        Eventually.HoldsAsync($"the service '{service}' in '{scope}' has no live instance", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync($"/api/scopes/{scope}");
            return document is not null && document.RootElement.GetProperty("services").EnumerateArray()
                .Where(s => s.GetProperty("name").GetString() == service)
                .All(s => s.GetProperty("liveInstances").GetInt32() == 0);
        });

    [When("the agent is stopped")]
    public Task WhenTheAgentIsStopped() => CommandAsync(MonitoringEnvironment.Agent, KnownResourceCommands.StopCommand, KnownResourceStates.Exited, KnownResourceStates.Finished);

    [When("the agent is started")]
    public Task WhenTheAgentIsStarted() => CommandAsync(MonitoringEnvironment.Agent, KnownResourceCommands.StartCommand, KnownResourceStates.Running);

    [When("the controller is restarted")]
    public async Task WhenTheControllerIsRestarted()
    {
        await CommandAsync(MonitoringEnvironment.Controller, KnownResourceCommands.StopCommand, KnownResourceStates.Exited, KnownResourceStates.Finished);
        await CommandAsync(MonitoringEnvironment.Controller, KnownResourceCommands.StartCommand, KnownResourceStates.Running);
    }

    [Then(@"the dashboard shows that no agent is connected for the scope ""(.*)"" within (\d+) seconds")]
    public Task ThenNoAgentIsConnected(string scope, int seconds) =>
        Eventually.HoldsAsync($"no agent is connected for '{scope}'", TimeSpan.FromSeconds(seconds), async () =>
        {
            using var document = await RunningEnvironment.GetJsonAsync($"/api/scopes/{scope}");
            return document is not null && !document.RootElement.GetProperty("agentConnected").GetBoolean();
        });

    private static async Task CommandAsync(string resource, string command, params string[] expectedStates)
    {
        var app = RunningEnvironment.App;
        var result = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(resource, command);
        if (!result.Success)
        {
            throw new InvalidOperationException($"'{command}' on '{resource}' failed: {result.Message}");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await app.ResourceNotifications.WaitForResourceAsync(resource, expectedStates, timeout.Token);
    }
}
