namespace Monitoring.Protocol.Instances;

/// <summary>What a container is doing now.</summary>
public enum ContainerPhase : byte
{
    /// <summary>The container has not started yet, or keeps failing to; <see cref="ContainerInfo.Reason"/> says why.</summary>
    Waiting,

    /// <summary>The container is running.</summary>
    Running,

    /// <summary>The container has exited.</summary>
    Terminated,
}
