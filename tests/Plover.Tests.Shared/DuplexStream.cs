using System.IO.Pipelines;

namespace Plover.Tests.Shared;

/// <summary>
/// One end of an in-memory, full-duplex connection: what one end writes, the other reads.
/// Lets connection code be tested without sockets.
/// </summary>
public sealed class DuplexStream : Stream
{
    private readonly Stream _input;
    private readonly Stream _output;

    private DuplexStream(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>Creates the two connected ends.</summary>
    /// <returns>The server end and the client end.</returns>
    public static (DuplexStream Server, DuplexStream Client) CreatePair()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var server = new DuplexStream(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        var client = new DuplexStream(toClient.Reader.AsStream(), toServer.Writer.AsStream());
        return (server, client);
    }

    /// <summary>Signals end of stream to the other end, as a closed connection would, while this end can still read.</summary>
    public void CompleteWriting() => _output.Dispose();

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _input.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _output.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override void Flush() => _output.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _output.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
            _output.Dispose();
        }

        base.Dispose(disposing);
    }
}
