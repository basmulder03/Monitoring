namespace Monitoring.Protocol.Messages;

/// <summary>Answer to a <see cref="Pull"/>.</summary>
/// <param name="RequestId">The <see cref="Pull.RequestId"/> being answered.</param>
/// <param name="Kind">The kind of data in the payload.</param>
/// <param name="Cursor">Position to pass in the next <see cref="Pull"/>.</param>
/// <param name="PayloadJson">The data as a JSON document whose shape depends on <paramref name="Kind"/>; <see langword="null"/> when nothing is new.</param>
public sealed record PullResult(long RequestId, PullKind Kind, long Cursor, string? PayloadJson) : Message;
