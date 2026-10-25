namespace Plover.Protocol.Instances;

/// <summary>What a container of a pod is there for.</summary>
public enum ContainerRole : byte
{
    /// <summary>The application itself.</summary>
    App,

    /// <summary>The sidecar of this monitoring system; when it fails the application is not reported as down.</summary>
    PloverSidecar,

    /// <summary>Any other sidecar, such as a proxy or log shipper.</summary>
    OtherSidecar,

    /// <summary>A container that runs before the application starts.</summary>
    Init,
}
