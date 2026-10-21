namespace Monitoring.Protocol.Messages;

/// <summary>Request from a parent for data newer than a cursor, so a reconnect never replays data.</summary>
/// <param name="RequestId">Correlates the matching <see cref="PullResult"/>.</param>
/// <param name="Kind">The kind of data requested.</param>
/// <param name="Cursor">Position returned by the previous result; 0 asks for everything available.</param>
public sealed record Pull(long RequestId, PullKind Kind, long Cursor) : Message;
