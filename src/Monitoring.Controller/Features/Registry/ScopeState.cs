using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Registry;

/// <summary>The instances of one scope and the last sequence number applied for it. Guarded by the registry's lock.</summary>
internal sealed class ScopeState
{
    /// <summary>The scope's instances by id, including those shown as Gone.</summary>
    public Dictionary<string, TrackedInstance> Instances { get; } = [];

    /// <summary>The labels the agent announced for the scope with its latest snapshot.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>Sequence number of the last snapshot or delta applied; replicas acknowledge it to the agent.</summary>
    public long LastSequence { get; set; }

    /// <summary>An instance together with the time it became Gone, if it did.</summary>
    /// <param name="Info">The instance as last reported.</param>
    /// <param name="GoneSince">When the instance became Gone, or <see langword="null"/> while it is live.</param>
    internal sealed record TrackedInstance(InstanceInfo Info, DateTimeOffset? GoneSince);
}
