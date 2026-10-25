namespace Plover.Protocol.Framing;

/// <summary>Raised when a peer sends a frame that is oversized or malformed.</summary>
public sealed class FrameException(string message) : IOException(message);
