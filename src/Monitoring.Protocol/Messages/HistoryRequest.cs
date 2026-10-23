namespace Monitoring.Protocol.Messages;

/// <summary>Request from a controller for the recent history of the metrics of one instance, sent only while someone looks at it.</summary>
/// <param name="RequestId">Correlates the matching <see cref="HistoryResult"/>.</param>
/// <param name="InstanceId">The instance, which is the pod name.</param>
public sealed record HistoryRequest(long RequestId, string InstanceId) : Message;
