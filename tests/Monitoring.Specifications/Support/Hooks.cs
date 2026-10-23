using Monitoring.AppHost;
using Reqnroll;

namespace Monitoring.Specifications.Support;

/// <summary>Leaves the shared environment as each scenario found it, whatever the scenario did and however it ended, so one scenario never depends on the clean-up of another.</summary>
/// <param name="applications">What the scenario broke.</param>
/// <param name="cluster">What the scenario deleted.</param>
/// <param name="processes">What the scenario stopped.</param>
[Binding]
public sealed class Hooks(ApplicationDriver applications, ClusterDriver cluster, ProcessDriver processes)
{
    /// <summary>Restores the environment after a scenario.</summary>
    /// <returns>A task that completes when it is restored.</returns>
    [AfterScenario]
    public async Task RestoreAsync()
    {
        applications.RepairAll();
        File.WriteAllText(MonitoringEnvironment.CentralConfiguration(RunningEnvironment.RunId), MonitoringEnvironment.DefaultCentralConfiguration);
        await processes.StartStoppedAsync();
        await cluster.RestoreAsync();
    }
}
