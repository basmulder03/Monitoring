using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Features.SelfMonitoring;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Common;
using Monitoring.Protocol.Instances;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>
/// Keeps the scope state in step with the pods of a namespace: lists them, then follows a watch, and lists them again
/// whenever the watch ends or fails so that a missed event can never leave the state wrong for long.
/// </summary>
/// <param name="api">The Kubernetes API.</param>
/// <param name="store">The scope state to keep current.</param>
/// <param name="target">The namespace to watch.</param>
/// <param name="options">Resync interval and retry delays.</param>
/// <param name="time">Clock for the waits.</param>
/// <param name="logger">Receives discovery failures.</param>
/// <param name="telemetry">Counts failures and re-lists; optional.</param>
internal sealed class KubernetesDiscoveryService(
    IKubernetesApi api,
    IScopeStateStore store,
    DiscoveryTarget target,
    KubernetesDiscoveryOptions options,
    TimeProvider time,
    ILogger<KubernetesDiscoveryService> logger,
    IAgentTelemetry? telemetry = null) : BackgroundService
{
    /// <summary>The instances currently in the store because of this service, by id.</summary>
    private readonly Dictionary<string, InstanceInfo> _known = [];

    private bool _listedBefore;

    /// <summary>The latest warning event per pod name, as of the last listing.</summary>
    private Dictionary<string, KubernetesEvent> _warnings = [];

    /// <summary>What each pod uses, as of the last listing; empty unless <see cref="KubernetesDiscoveryOptions.PodMetrics"/> is on.</summary>
    private Dictionary<string, PodUsage> _usage = [];

    /// <summary>What each Deployment and StatefulSet says about its rollout, for the ones that have something to say, as of the last listing.</summary>
    private Dictionary<string, string> _rollouts = [];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                var started = time.GetTimestamp();
                var version = await ListAsync(stoppingToken).ConfigureAwait(false);
                failures = 0;
                await WatchAsync(version, stoppingToken).ConfigureAwait(false);

                // The server ended the watch after the resync interval, which is expected. Only a watch that
                // ended at once is held back, so a misbehaving server cannot make this loop spin.
                var elapsed = time.GetElapsedTime(started);
                wait = elapsed >= options.RetryMinDelay ? TimeSpan.Zero : options.RetryMinDelay - elapsed;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (KubernetesResourceExpiredException)
            {
                telemetry?.Add("monitoring.agent.kubernetes_relists");
                logger.LogInformation("The pod watch fell too far behind; listing the pods of '{Namespace}' again", target.Namespace);
                wait = options.RetryMinDelay;
            }
            catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or IOException)
            {
                telemetry?.Add("monitoring.agent.kubernetes_errors");
                logger.LogWarning("Cannot follow the pods of '{Namespace}': {Reason}", target.Namespace, ex.Message);
                wait = Backoff.Delay(failures, options.RetryMinDelay, options.RetryMaxDelay, Random.Shared.NextDouble());
                failures++;
            }

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Lists the pods and makes the store match them; returns the version to watch from.</summary>
    private async Task<string> ListAsync(CancellationToken ct)
    {
        var list = await api.ListPodsAsync(target.Namespace, ct).ConfigureAwait(false);
        _warnings = await ListWarningsAsync(ct).ConfigureAwait(false);
        _rollouts = await ListRolloutsAsync(ct).ConfigureAwait(false);
        _usage = await ListUsageAsync(ct).ConfigureAwait(false);

        var current = new Dictionary<string, InstanceInfo>();
        foreach (var pod in list.Pods)
        {
            if (PodMapper.Map(pod, options.MonitoringSidecarContainer, _warnings.GetValueOrDefault(pod.Metadata.Name), RolloutOf(pod), _usage.GetValueOrDefault(pod.Metadata.Name)) is { } instance)
            {
                current[instance.Id] = instance;
            }
        }

        foreach (var instance in current.Values)
        {
            Upsert(instance);
        }

        foreach (var id in _known.Keys.Where(id => !current.ContainsKey(id)).ToList())
        {
            Remove(id);
        }

        store.Publish();

        if (!_listedBefore)
        {
            _listedBefore = true;
            logger.LogInformation("Following {Count} pods in namespace '{Namespace}'", current.Count, target.Namespace);
        }

        return list.ResourceVersion;
    }

    /// <summary>
    /// Reads the latest warning event per pod. Events explain pods that never start, but the agent works without them, for example when its role
    /// may not read events, so a failure only costs that detail.
    /// </summary>
    private async Task<Dictionary<string, KubernetesEvent>> ListWarningsAsync(CancellationToken ct)
    {
        try
        {
            var events = await api.ListWarningEventsAsync(target.Namespace, ct).ConfigureAwait(false);
            return events
                .Where(e => e.InvolvedObject is { Kind: "Pod", Name: { } })
                .GroupBy(e => e.InvolvedObject!.Name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.MaxBy(e => e.LastTimestamp)!, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or IOException)
        {
            logger.LogDebug("Cannot read the warning events of '{Namespace}': {Reason}", target.Namespace, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Reads what the pods use, when asked to. A cluster without a metrics server, or a role that may not read it, only costs that detail, so it is said once and then tried again quietly.
    /// </summary>
    private async Task<Dictionary<string, PodUsage>> ListUsageAsync(CancellationToken ct)
    {
        if (!options.PodMetrics)
        {
            return [];
        }

        try
        {
            var usage = await api.ListPodUsageAsync(target.Namespace, ct).ConfigureAwait(false);
            _usageUnavailableReported = false;
            return new Dictionary<string, PodUsage>(usage, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or IOException)
        {
            if (!_usageUnavailableReported)
            {
                _usageUnavailableReported = true;
                logger.LogWarning("Cannot read the use of the pods of '{Namespace}' from the metrics API, so they are shown without it: {Reason}", target.Namespace, ex.Message);
            }

            return [];
        }
    }

    private bool _usageUnavailableReported;

    private string? RolloutOf(Pod pod) => Rollout.KeyOf(pod) is { } key ? _rollouts.GetValueOrDefault(key) : null;

    /// <summary>
    /// Reads which Deployments and StatefulSets are changing or short of pods. Like the events this is a detail that the agent can do without, for example when its role
    /// may not read workloads, so a failure only costs that.
    /// </summary>
    private async Task<Dictionary<string, string>> ListRolloutsAsync(CancellationToken ct)
    {
        try
        {
            var workloads = await api.ListWorkloadsAsync(target.Namespace, ct).ConfigureAwait(false);
            return workloads
                .Select(pair => (pair.Key, Text: Rollout.Describe(pair.Value)))
                .Where(entry => entry.Text is not null)
                .ToDictionary(entry => entry.Key, entry => entry.Text!, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is KubernetesApiException or HttpRequestException or IOException)
        {
            logger.LogDebug("Cannot read the workloads of '{Namespace}': {Reason}", target.Namespace, ex.Message);
            return [];
        }
    }

    /// <summary>Applies the changes of a watch to the store until the watch ends.</summary>
    private async Task WatchAsync(string version, CancellationToken ct)
    {
        await foreach (var podEvent in api.WatchPodsAsync(target.Namespace, version, options.ResyncInterval, ct).ConfigureAwait(false))
        {
            switch (podEvent.Type)
            {
                case PodEventType.Added or PodEventType.Modified when podEvent.Pod is { } pod:
                    if (PodMapper.Map(pod, options.MonitoringSidecarContainer, _warnings.GetValueOrDefault(pod.Metadata.Name), RolloutOf(pod), _usage.GetValueOrDefault(pod.Metadata.Name)) is { } instance)
                    {
                        Upsert(instance);
                    }
                    else
                    {
                        Remove(pod.Metadata.Name);
                    }

                    break;

                case PodEventType.Deleted when podEvent.Pod is { } deleted:
                    Remove(deleted.Metadata.Name);
                    break;
            }

            store.Publish();
        }
    }

    /// <summary>Adds or updates an instance, but only tells the store when something actually changed.</summary>
    private void Upsert(InstanceInfo instance)
    {
        if (_known.TryGetValue(instance.Id, out var existing) && SameInstance(existing, instance))
        {
            return;
        }

        _known[instance.Id] = instance;
        store.Upsert(instance);
    }

    private void Remove(string id)
    {
        if (_known.Remove(id))
        {
            store.Remove(id);
        }
    }

    private static bool SameInstance(InstanceInfo a, InstanceInfo b) =>
        a.Id == b.Id
        && a.ServiceName == b.ServiceName
        && a.Lifecycle == b.Lifecycle
        && a.Health == b.Health
        && (a.Checks ?? []).SequenceEqual(b.Checks ?? [])
        && a.Labels.Count == b.Labels.Count
        && a.Labels.All(label => b.Labels.TryGetValue(label.Key, out var value) && value == label.Value);
}
