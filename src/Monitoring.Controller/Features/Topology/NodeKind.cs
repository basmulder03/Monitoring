namespace Monitoring.Controller.Features.Topology;

/// <summary>What a node of the dependency graph stands for.</summary>
public enum NodeKind : byte
{
    /// <summary>A service that reports to the monitoring system.</summary>
    Service,

    /// <summary>A database server or database.</summary>
    Database,

    /// <summary>A queue or topic of a message broker.</summary>
    Queue,

    /// <summary>Something that was called but is not a known service, such as a third-party API.</summary>
    External,
}
