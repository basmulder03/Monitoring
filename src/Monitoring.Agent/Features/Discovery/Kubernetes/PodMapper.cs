using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>Turns a Kubernetes pod into the instance the controller shows.</summary>
internal static class PodMapper
{
    /// <summary>The name the Helm template gives the monitoring sidecar container.</summary>
    public const string DefaultMonitoringSidecar = "monitoring-sidecar";

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
    /// <param name="monitoringSidecar">Name of the container that is this system's own sidecar, whose failure must not make the application look down.</param>
    /// <returns>The instance, or <see langword="null"/> for a pod that completed successfully, which is not an outage and not worth showing.</returns>
    public static InstanceInfo? Map(Pod pod, string monitoringSidecar = DefaultMonitoringSidecar)
    {
        if (pod.Status?.Phase == "Succeeded")
        {
            return null;
        }

        var containers = ContainersOf(pod, monitoringSidecar);
        var sidecar = containers.FirstOrDefault(c => c.Role == ContainerRole.MonitoringSidecar);
        return new InstanceInfo(
            pod.Metadata.Name,
            ServiceNameOf(pod),
            LifecycleOf(pod),
            HealthOf(pod, monitoringSidecar),
            LabelsOf(pod),
            Checks: sidecar is { Health: HealthState.Unhealthy or HealthState.Degraded }
                ? [new HealthCheckResult("monitoring sidecar", HealthState.Degraded, sidecar.Reason ?? "The monitoring sidecar is not ready.")]
                : null,
            Containers: containers.Count > 0 ? containers : null);
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

    private static HealthState HealthOf(Pod pod, string monitoringSidecar)
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

        // A failing monitoring sidecar says nothing about the application, so it only degrades the instance.
        var containers = pod.Status?.ContainerStatuses ?? [];
        if (containers.Where(c => c.Name != monitoringSidecar).Any(IsFailing))
        {
            return HealthState.Unhealthy;
        }

        var ready = pod.Status?.Conditions?.Any(c => c.Type == "Ready" && c.Status == "True") == true;
        if (containers.Any(IsFailing))
        {
            return phase == "Running" ? HealthState.Degraded : HealthState.Unknown;
        }

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

    /// <summary>Lists the containers of a pod with their role and state. Init containers that finished without error are left out.</summary>
    private static List<ContainerInfo> ContainersOf(Pod pod, string monitoringSidecar)
    {
        var result = new List<ContainerInfo>();
        var initSpecs = pod.Spec?.InitContainers ?? [];
        foreach (var status in pod.Status?.InitContainerStatuses ?? [])
        {
            var nativeSidecar = initSpecs.Any(c => c.Name == status.Name && c.RestartPolicy == "Always");
            var role = status.Name == monitoringSidecar ? ContainerRole.MonitoringSidecar
                : nativeSidecar ? ContainerRole.OtherSidecar : ContainerRole.Init;
            if (role == ContainerRole.Init && status.State?.Terminated is { ExitCode: 0 })
            {
                continue;
            }

            result.Add(Describe(status, role));
        }

        var specs = pod.Spec?.Containers ?? [];
        var app = specs.FirstOrDefault(c => c.Name != monitoringSidecar)?.Name;
        var statuses = pod.Status?.ContainerStatuses ?? [];
        foreach (var spec in specs)
        {
            var role = spec.Name == monitoringSidecar ? ContainerRole.MonitoringSidecar
                : spec.Name == app ? ContainerRole.App : ContainerRole.OtherSidecar;

            // A pod that has no status yet still has the containers it was told to run, waiting to start.
            var status = statuses.FirstOrDefault(s => s.Name == spec.Name) ?? new ContainerStatus(spec.Name, Image: spec.Image);
            result.Add(Describe(status, role));
        }

        return result;
    }

    private static ContainerInfo Describe(ContainerStatus status, ContainerRole role)
    {
        var state = status.State;
        var phase = state?.Running is not null ? ContainerPhase.Running
            : state?.Terminated is not null ? ContainerPhase.Terminated
            : ContainerPhase.Waiting;

        var health = phase switch
        {
            ContainerPhase.Running => status.Ready ? HealthState.Healthy : HealthState.Degraded,
            ContainerPhase.Terminated => state!.Terminated!.ExitCode == 0 ? HealthState.Healthy : HealthState.Unhealthy,
            _ => state?.Waiting is { Reason: { } reason } && FailingWaitingReasons.Contains(reason) ? HealthState.Unhealthy : HealthState.Unknown,
        };

        var last = status.LastState?.Terminated;
        return new ContainerInfo(
            status.Name,
            role,
            phase,
            health,
            status.Ready,
            status.RestartCount,
            status.Image,
            status.ImageID,
            Reason: state?.Waiting?.Reason ?? state?.Terminated?.Reason,
            Message: state?.Waiting?.Message,
            ExitCode: state?.Terminated?.ExitCode,
            LastExitCode: last?.ExitCode,
            LastReason: last?.Reason);
    }
}
