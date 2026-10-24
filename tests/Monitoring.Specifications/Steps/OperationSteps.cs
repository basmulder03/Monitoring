using Monitoring.AppHost;
using Monitoring.Specifications.Support;
using Reqnroll;

namespace Monitoring.Specifications.Steps;

/// <summary>What users and operators do to the system: call applications, break them, delete pods, stop processes.</summary>
[Binding]
public sealed class OperationSteps(ApplicationDriver applications, ClusterDriver cluster, ProcessDriver processes)
{
    [When(@"the application ""(.*)"" breaks")]
    public void WhenTheApplicationBreaks(string application) => applications.Break(application);

    [When(@"the application ""(.*)"" is repaired")]
    public void WhenTheApplicationIsRepaired(string application) => applications.Repair(application);

    [When(@"the application ""(.*)"" calls the application ""(.*)"" (\d+) times")]
    public Task WhenTheApplicationCallsTheApplication(string caller, string callee, int times) => applications.CallAsync(caller, callee, times);

    [When(@"the application ""(.*)"" is asked for an order (\d+) times")]
    public Task WhenTheApplicationIsAskedForAnOrder(string application, int times) => applications.GetAsync(application, "/order", times);

    [When(@"the pod ""(.*)"" of ""(.*)"" is deleted")]
    public Task WhenThePodIsDeleted(string pod, string service) => cluster.DeleteAsync(pod);

    [When(@"the pod ""(.*)"" of ""(.*)"" is created")]
    public Task WhenThePodIsCreated(string pod, string service) => cluster.PutAsync(pod, service);

    [When(@"the pod ""(.*)"" of ""(.*)"" is no longer ready")]
    public Task WhenThePodIsNotReady(string pod, string service) => cluster.PutAsync(pod, service, ready: false);

    [When(@"the pod ""(.*)"" of ""(.*)"" is ready again")]
    public Task WhenThePodIsReadyAgain(string pod, string service) => cluster.PutAsync(pod, service, ready: true);

    [When(@"the application of the pod ""(.*)"" of ""(.*)"" is killed for lack of memory again and again")]
    public Task WhenTheApplicationKeepsBeingKilled(string pod, string service) => cluster.PutAsync(pod, service, ready: false, fault: "crash");

    [When(@"the monitoring sidecar of the pod ""(.*)"" of ""(.*)"" crash loops")]
    public Task WhenTheSidecarCrashLoops(string pod, string service) => cluster.PutAsync(pod, service, ready: false, fault: "sidecar");

    [When("the agent is stopped")]
    public Task WhenTheAgentIsStopped() => processes.StopAsync(MonitoringEnvironment.Agent);

    [When("the agent is started")]
    public Task WhenTheAgentIsStarted() => processes.StartAsync(MonitoringEnvironment.Agent);

    [When("the controller is restarted")]
    public async Task WhenTheControllerIsRestarted()
    {
        await processes.StopAsync(MonitoringEnvironment.Controller);
        await processes.StartAsync(MonitoringEnvironment.Controller);
    }

    [When(@"the sidecar of ""(.*)"" is stopped")]
    public Task WhenTheSidecarIsStopped(string application) => processes.StopAsync($"{application}-sidecar");

    [When(@"the sidecar of ""(.*)"" is started")]
    public Task WhenTheSidecarIsStarted(string application) => processes.StartAsync($"{application}-sidecar");
}
