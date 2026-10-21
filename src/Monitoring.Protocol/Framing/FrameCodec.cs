using System.Buffers.Binary;
using System.Text.Json;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Serialization;

namespace Monitoring.Protocol.Framing;

/// <summary>
/// Length-prefixed JSON frames: a 4-byte big-endian payload length followed by UTF-8 JSON of one <see cref="Message"/>.
/// </summary>
public static class FrameCodec
{
    /// <summary>Size in bytes of the length prefix.</summary>
    public const int HeaderSize = 4;
    /// <summary>Default upper bound for a frame payload, which protects a peer from unbounded allocation.</summary>
    public const int DefaultMaxFrameBytes = 1024 * 1024;

    /// <summary>Writes one message as a single frame and flushes the stream.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="message">Message to send.</param>
    /// <param name="maxFrameBytes">Largest payload allowed.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <exception cref="FrameException">The serialized payload exceeds <paramref name="maxFrameBytes"/>.</exception>
    public static async ValueTask WriteAsync(
        Stream stream, Message message, int maxFrameBytes = DefaultMaxFrameBytes, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.Message);
        if (payload.Length > maxFrameBytes)
        {
            throw new FrameException($"Frame of {payload.Length} bytes exceeds the limit of {maxFrameBytes}.");
        }

        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, HeaderSize);
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads the next message, or returns null when the peer closed the stream between frames.</summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="maxFrameBytes">Largest payload accepted; checked before any buffer is allocated.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The message, or <see langword="null"/> on a clean end of stream.</returns>
    /// <exception cref="FrameException">The frame is oversized, truncated or not valid JSON for a message.</exception>
    public static async ValueTask<Message?> ReadAsync(
        Stream stream, int maxFrameBytes = DefaultMaxFrameBytes, CancellationToken ct = default)
    {
        var header = new byte[HeaderSize];
        if (!await ReadExactlyOrEndAsync(stream, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maxFrameBytes)
        {
            throw new FrameException($"Invalid frame length {length}; the limit is {maxFrameBytes}.");
        }

        var payload = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, payload, ct).ConfigureAwait(false))
        {
            throw new FrameException("Stream ended in the middle of a frame.");
        }

        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.Message)
                ?? throw new FrameException("Frame contained a null message.");
        }
        // A missing or unknown "type" discriminator surfaces as NotSupportedException, not JsonException.
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new FrameException($"Malformed frame: {ex.Message}");
        }
    }

    /// <summary>Fills the buffer; returns false only if the stream ended before the first byte.</summary>
    private static async ValueTask<bool> ReadExactlyOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false;
                }
                throw new FrameException("Stream ended in the middle of a frame.");
            }
            read += n;
        }
        return true;
    }
}
