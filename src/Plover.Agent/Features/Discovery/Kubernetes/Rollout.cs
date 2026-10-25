using Plover.Agent.Features.Discovery.Kubernetes.Models;

namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>Works out from a Deployment or a StatefulSet whether it is changing or short of pods, so that every pod of it can say so.</summary>
internal static class Rollout
{
    /// <summary>Tells what is going on with a workload.</summary>
    /// <param name="workload">The workload.</param>
    /// <returns>A sentence, or <see langword="null"/> when it has as many ready pods of the latest version as it wants.</returns>
    public static string? Describe(KubernetesWorkload workload)
    {
        var desired = workload.Spec?.Replicas ?? 1;
        var ready = workload.Status?.ReadyReplicas ?? 0;
        var updated = workload.Status?.UpdatedReplicas ?? 0;
        if (desired == 0)
        {
            return null;
        }

        if (workload.Metadata?.Generation is { } generation && workload.Status?.ObservedGeneration is { } seen && seen < generation)
        {
            return "A change is being rolled out.";
        }

        if (updated < desired)
        {
            return $"Rolling out: {updated} of {desired} replicas updated, {ready} ready.";
        }

        // More pods than wanted means the old ones are still there: the new ones, which count as updated before they are ready, have not taken over yet.
        if (workload.Status?.Replicas is { } total && total > desired)
        {
            return $"Rolling out: {total} replicas exist for {desired} wanted, {ready} ready.";
        }

        return ready < desired ? $"{ready} of {desired} replicas are ready." : null;
    }

    /// <summary>The key that a pod and its workload share: the kind and the name, where a Deployment is found through the ReplicaSet that owns the pod.</summary>
    /// <param name="pod">The pod.</param>
    /// <returns>The key, or <see langword="null"/> for a pod that no Deployment or StatefulSet owns.</returns>
    public static string? KeyOf(Pod pod)
    {
        var owner = pod.Metadata.OwnerReferences?.FirstOrDefault(o => o.Controller == true) ?? pod.Metadata.OwnerReferences?.FirstOrDefault();
        if (owner is null)
        {
            return null;
        }

        // A Deployment's pods are owned by a ReplicaSet named "<deployment>-<hash>".
        var dash = owner.Name.LastIndexOf('-');
        return owner.Kind switch
        {
            "ReplicaSet" when dash > 0 => $"Deployment/{owner.Name[..dash]}",
            "StatefulSet" => $"StatefulSet/{owner.Name}",
            _ => null,
        };
    }
}
