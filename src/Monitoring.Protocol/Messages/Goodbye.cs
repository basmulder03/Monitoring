namespace Monitoring.Protocol.Messages;

/// <summary>Graceful shutdown, so the entity is removed instead of marked unhealthy.</summary>
/// <param name="InstanceId">The instance that is shutting down.</param>
public sealed record Goodbye(string InstanceId) : Message;
