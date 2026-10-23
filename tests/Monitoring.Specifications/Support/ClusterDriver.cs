using Aspire.Hosting.Testing;
using Monitoring.AppHost;

namespace Monitoring.Specifications.Support;

/// <summary>Changes the pods of the fake Kubernetes cluster, which the agent follows as it would a real one.</summary>
public sealed class ClusterDriver
{
    private readonly HashSet<string> _deleted = [];
    private readonly HashSet<string> _changed = [];

    /// <summary>Removes a pod.</summary>
    /// <param name="pod">The pod.</param>
    /// <returns>A task that completes when the cluster has done it.</returns>
    public async Task DeleteAsync(string pod)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        using var response = await client.DeleteAsync($"/control/pods/{pod}");
        response.EnsureSuccessStatusCode();
        _deleted.Add(pod);
    }

    /// <summary>Adds a pod, or changes it.</summary>
    /// <param name="pod">The pod.</param>
    /// <param name="service">The service its application is.</param>
    /// <param name="ready">Whether it is ready.</param>
    /// <returns>A task that completes when the cluster has done it.</returns>
    public async Task PutAsync(string pod, string service, bool ready = true)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        using var response = await client.PutAsync($"/control/pods/{pod}?service={service}&ready={ready.ToString().ToLowerInvariant()}", content: null);
        response.EnsureSuccessStatusCode();
        _deleted.Remove(pod);
        if (!ready)
        {
            _changed.Add(pod);
        }
        else
        {
            _changed.Remove(pod);
        }
    }

    /// <summary>Puts back every pod this scenario deleted or made unready, so the next one finds the cluster as it was.</summary>
    /// <returns>A task that completes when they are back.</returns>
    public async Task RestoreAsync()
    {
        foreach (var pod in _deleted.Concat(_changed).ToList())
        {
            await PutAsync(pod, pod[..pod.LastIndexOf('-')]);
        }
    }
}
