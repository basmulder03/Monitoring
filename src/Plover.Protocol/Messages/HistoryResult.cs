namespace Plover.Protocol.Messages;

/// <summary>Answer to a <see cref="HistoryRequest"/>.</summary>
/// <param name="RequestId">The <see cref="HistoryRequest.RequestId"/> being answered.</param>
/// <param name="PayloadJson">A <see cref="Reports.MetricHistory"/> as JSON; <see langword="null"/> when the agent keeps no history of that instance.</param>
public sealed record HistoryResult(long RequestId, string? PayloadJson) : Message;
