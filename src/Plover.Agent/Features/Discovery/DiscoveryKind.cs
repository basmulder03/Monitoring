namespace Plover.Agent.Features.Discovery;

/// <summary>Where the agent learns which instances exist in its scope.</summary>
public enum DiscoveryKind : byte
{
    /// <summary>Nothing is discovered; instances come from elsewhere or the scope stays empty.</summary>
    None,

    /// <summary>The pods of a Kubernetes namespace, through the Kubernetes API.</summary>
    Kubernetes,

    /// <summary>The Windows services of the machine the agent runs on, through the Service Control Manager.</summary>
    Windows,
}
