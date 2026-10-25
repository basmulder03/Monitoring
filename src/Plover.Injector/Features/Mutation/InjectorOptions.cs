namespace Plover.Injector.Features.Mutation;

/// <summary>Settings of the injection: what the sidecar looks like and where it reports.</summary>
public sealed class InjectorOptions
{
    /// <summary>The label that opts a pod, or its namespace, in; the webhook registration selects on it, and the pod can opt out again with <c>false</c>.</summary>
    public const string InjectLabel = "plover/inject";

    /// <summary>The name of the sidecar container; the agent knows its own sidecar by it.</summary>
    public const string SidecarContainer = "plover-sidecar";

    /// <summary>Gets or sets the image of the sidecar, which has to be given.</summary>
    public string SidecarImage { get; set; } = "";

    /// <summary>Gets or sets when the image is pulled.</summary>
    public string ImagePullPolicy { get; set; } = "IfNotPresent";

    /// <summary>Gets or sets the address of the agent of the namespace, as <c>host:port</c>.</summary>
    public string AgentAddress { get; set; } = "plover-agent:9441";

    /// <summary>Gets or sets whether the sidecar is a native sidecar (Kubernetes 1.29 and later) or an ordinary second container.</summary>
    public bool NativeSidecar { get; set; } = true;

    /// <summary>Gets or sets free-form labels the sidecar sends with the registration.</summary>
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.Ordinal);
}
