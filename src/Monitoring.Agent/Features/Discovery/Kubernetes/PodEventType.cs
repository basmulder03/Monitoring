namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>What happened to a pod, as reported by a watch.</summary>
internal enum PodEventType : byte
{
    /// <summary>The pod was created.</summary>
    Added,

    /// <summary>The pod changed.</summary>
    Modified,

    /// <summary>The pod was deleted.</summary>
    Deleted,

    /// <summary>Nothing happened to a pod; the watch only reports how far it has read.</summary>
    Bookmark,
}
