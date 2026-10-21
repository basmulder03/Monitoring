namespace Monitoring.Protocol.Instances;

/// <summary>Where an instance is in its lifetime; independent of its health.</summary>
public enum InstanceLifecycle : byte
{
    /// <summary>The pod or process exists but has not registered yet.</summary>
    Starting,

    /// <summary>The instance is registered and running.</summary>
    Running,

    /// <summary>The instance is shutting down; a graceful goodbye avoids a false unhealthy alert.</summary>
    Terminating,

    /// <summary>The instance was deleted; kept briefly so the UI can show what disappeared.</summary>
    Gone,
}
