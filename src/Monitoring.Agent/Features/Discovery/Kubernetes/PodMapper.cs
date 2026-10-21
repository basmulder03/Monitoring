using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>Turns a Kubernetes pod into the instance the controller shows.</summary>
internal static class PodMapper
{
    /// <summary>Container waiting reasons that mean the container is stuck rather than starting normally.</summary>
    private static readonly HashSet<string> FailingWaitingReasons = new(StringComparer.Ordinal)
    {
        "CrashLoopBackOff",
        "ImagePullBackOff",
        "ErrImagePull",
        "ErrImageNeverPull",
        "InvalidImageName",
        "CreateContainerConfigError",
        "CreateContainerError",
        "RunContainerError",
    };

    /// <summary>Maps a pod to an instance.</summary>
    /// <param name="pod">The pod as the Kubernetes API reports it.</param>
    /// <returns>The instance, or <see langword="null"/> for a pod that completed successfully, which is not an outage and not worth showing.</returns>
    public static InstanceInfo? Map(Pod pod)
    {
        if (pod.Status?.Phase == "Succeeded")
        {
            return null;
        }

        return new InstanceInfo(
            pod.Metadata.Name,
            ServiceNameOf(pod),
            LifecycleOf(pod),
            HealthOf(pod),
            LabelsOf(pod));
    }

    /// <summary>Names the service a pod belongs to: its name label, else the workload that owns it, else the pod itself.</summary>
    private static string ServiceNameOf(Pod pod)
    {
        var labels = pod.Metadata.Labels;
        foreach (var key in new[] { "app.kubernetes.io/name", "app" })
        {
            if (labels is not null && labels.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        var owner = pod.Metadata.OwnerReferences?.FirstOrDefault(o => o.Controller == true)
            ?? pod.Metadata.OwnerReferences?.FirstOrDefault();
        if (owner is null)
        {
            return pod.Metadata.Name;
        }

        // A Deployment's pods are owned by a ReplicaSet named "<deployment>-<hash>".
        var dash = owner.Name.LastIndexOf('-');
        return owner.Kind == "ReplicaSet" && dash > 0 ? owner.Name[..dash] : owner.Name;
    }

    private static InstanceLifecycle LifecycleOf(Pod pod)
    {
        if (pod.Metadata.DeletionTimestamp is not null || pod.Status?.Phase == "Failed")
        {
            return InstanceLifecycle.Terminating;
        }

        return pod.Status?.Phase == "Pending" ? InstanceLifecycle.Starting : InstanceLifecycle.Running;
    }

    private static HealthState HealthOf(Pod pod)
    {
        var phase = pod.Status?.Phase;
        if (phase == "Failed")
        {
            return HealthState.Unhealthy;
        }

        if (phase == "Unknown")
        {
            return HealthState.Unknown;
        }

        var containers = pod.Status?.ContainerStatuses ?? [];
        if (containers.Any(IsFailing))
        {
            return HealthState.Unhealthy;
        }

        var ready = pod.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true;
        if (ready && containers.All(c => c.Ready))
        {
            return HealthState.Healthy;
        }

        // Not ready: a pod that is still starting has nothing to judge yet, a running one is degraded.
        return phase == "Running" ? HealthState.Degraded : HealthState.Unknown;
    }

    private static bool IsFailing(ContainerStatus container) =>
        container.State?.Waiting is { Reason: { } reason } && FailingWaitingReasons.Contains(reason)
        || container.State?.Terminated is { ExitCode: not 0 };

    private static Dictionary<string, string> LabelsOf(Pod pod)
    {
        var labels = new Dictionary<string, string>(pod.Metadata.Labels ?? []);
        if (pod.Spec?.NodeName is { Length: > 0 } node)
        {
            labels["k8s.node"] = node;
        }

        if (pod.Status?.PodIP is { Length: > 0 } ip)
        {
            labels["k8s.pod-ip"] = ip;
        }

        return labels;
    }
}
