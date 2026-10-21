using System.Buffers.Binary;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Protocol.Tests.Framing;

public class FrameCodecTests
{
    private static readonly Dictionary<string, string> Labels = new() { ["env"] = "prod", ["site"] = "a" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<Message> AllMessages() => new()
    {
        new Hello(1, "pod-1", "orders", "1.2.3", Labels, "secret", "shop"),
        new Hello(1, "pod-2", "orders", null, Labels),
        new Heartbeat(42, HealthState.Degraded),
        new Pull(7, PullKind.Metrics, 100),
        new PullResult(7, PullKind.Metrics, 120, "{\"a\":1}"),
        new Snapshot(1, "shop", [new InstanceInfo("pod-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, Labels)], Labels),
        new Snapshot(2, "shop", []),
        new Snapshot(3, "shop", [new InstanceInfo("pod-1", "orders", InstanceLifecycle.Running, HealthState.Degraded, Labels, "1.4.2", [new HealthCheckResult("database", HealthState.Degraded, "slow")])]),
        new Delta(2, [], ["pod-1"]),
        new Goodbye("pod-1"),
        new Config("abc123", ["tcp://c1:9000", "tcp://c2:9000"]),
        new Ack(12, SnapshotRequired: true),
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public async Task ReadAsync_returns_the_message_type_that_was_written(Message message)
    {
        // Arrange
        var stream = await new FrameStreamBuilder().WithMessage(message).BuildAsync();

        // Act
        var read = await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        read.ShouldBeOfType(message.GetType());
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public async Task ReadAsync_restores_every_field_of_the_message(Message message)
    {
        // Arrange
        var written = await new FrameStreamBuilder().WithMessage(message).BuildAsync();
        var original = written.ToArray();

        // Act
        var read = await FrameCodec.ReadAsync(written, ct: Ct);
        var again = await new FrameStreamBuilder().WithMessage(read!).BuildAsync();

        // Assert: records compare dictionaries and lists by reference, so compare the re-encoded frame instead
        again.ToArray().ShouldBe(original);
    }

    [Fact]
    public async Task ReadAsync_accepts_a_hello_from_a_sender_that_does_not_announce_a_scope()
    {
        // Arrange: a hello as an older sender wrote it, without a scope field
        var stream = await new FrameStreamBuilder()
            .WithRawPayload("""{"type":"hello","protocolVersion":1,"instanceId":"a","serviceName":"s","labels":{}}""")
            .BuildAsync();

        // Act
        var read = await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        read.ShouldBeOfType<Hello>().ScopeName.ShouldBeNull();
    }

    [Fact]
    public async Task ReadAsync_restores_the_version_and_checks_of_an_instance()
    {
        // Arrange
        var instance = new InstanceInfo("pod-1", "orders", InstanceLifecycle.Running, HealthState.Degraded, Labels, "1.4.2", [new HealthCheckResult("database", HealthState.Degraded, "slow")]);
        var stream = await new FrameStreamBuilder().WithMessage(new Delta(1, [instance], [])).BuildAsync();

        // Act
        var read = (Delta)(await FrameCodec.ReadAsync(stream, ct: Ct))!;

        // Assert
        var restored = read.Upserted.Single();
        restored.Version.ShouldBe("1.4.2");
        restored.Checks!.Single().Name.ShouldBe("database");
        restored.Checks!.Single().Description.ShouldBe("slow");
    }

    [Fact]
    public async Task ReadAsync_accepts_an_instance_from_a_sender_that_knows_neither_version_nor_checks()
    {
        // Arrange: an instance as an older sender wrote it
        var stream = await new FrameStreamBuilder()
            .WithRawPayload("""{"type":"delta","sequence":1,"upserted":[{"id":"a","serviceName":"s","lifecycle":"Running","health":"Healthy","labels":{}}],"removed":[]}""")
            .BuildAsync();

        // Act
        var read = (Delta)(await FrameCodec.ReadAsync(stream, ct: Ct))!;

        // Assert
        read.Upserted.Single().Version.ShouldBeNull();
        read.Upserted.Single().Checks.ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_prefixes_the_payload_length_as_big_endian()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        await FrameCodec.WriteAsync(stream, new Goodbye("x"), ct: Ct);

        // Assert
        var bytes = stream.ToArray();
        BinaryPrimitives.ReadInt32BigEndian(bytes).ShouldBe(bytes.Length - FrameCodec.HeaderSize);
    }

    [Fact]
    public async Task WriteAsync_rejects_a_payload_over_the_limit()
    {
        // Arrange
        var message = new PullResult(1, PullKind.Spans, 0, new string('x', 500));

        // Act
        var write = async () => await FrameCodec.WriteAsync(new MemoryStream(), message, maxFrameBytes: 100, ct: Ct);

        // Assert
        await write.ShouldThrowAsync<FrameException>();
    }

    [Fact]
    public async Task ReadAsync_returns_consecutive_frames_in_order()
    {
        // Arrange
        var stream = await new FrameStreamBuilder()
            .WithMessage(new Heartbeat(1, HealthState.Healthy))
            .WithMessage(new Heartbeat(2, HealthState.Healthy))
            .BuildAsync();

        // Act
        var first = await FrameCodec.ReadAsync(stream, ct: Ct);
        var second = await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        ((Heartbeat)first!).Sequence.ShouldBe(1);
        ((Heartbeat)second!).Sequence.ShouldBe(2);
    }

    [Fact]
    public async Task ReadAsync_returns_null_when_the_stream_ends_between_frames()
    {
        // Arrange
        var stream = new MemoryStream();

        // Act
        var read = await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        read.ShouldBeNull();
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(FrameCodec.DefaultMaxFrameBytes + 1)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsync_rejects_an_invalid_length_prefix(int length)
    {
        // Arrange
        var stream = await new FrameStreamBuilder().WithLengthPrefix(length).BuildAsync();

        // Act
        var read = async () => await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        await read.ShouldThrowAsync<FrameException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(FrameCodec.HeaderSize + 1)]
    public async Task ReadAsync_rejects_a_frame_cut_short(int bytesRemoved)
    {
        // Arrange
        var truncated = await new FrameStreamBuilder().WithMessage(new Goodbye("x")).WithoutLastBytes(bytesRemoved).BuildAsync();

        // Act
        var read = async () => await FrameCodec.ReadAsync(truncated, ct: Ct);

        // Assert
        await read.ShouldThrowAsync<FrameException>();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"unknown\"}")]
    public async Task ReadAsync_rejects_a_payload_that_is_not_a_message(string payloadText)
    {
        // Arrange
        var stream = await new FrameStreamBuilder().WithRawPayload(payloadText).BuildAsync();

        // Act
        var read = async () => await FrameCodec.ReadAsync(stream, ct: Ct);

        // Assert
        await read.ShouldThrowAsync<FrameException>();
    }

    /// <summary>Builds a stream of frames positioned at its start, ready for <see cref="FrameCodec.ReadAsync"/>.</summary>
    private sealed class FrameStreamBuilder
    {
        private readonly List<Func<MemoryStream, ValueTask>> _parts = [];
        private int _bytesToRemove;

        /// <summary>Appends a message encoded by <see cref="FrameCodec"/>.</summary>
        public FrameStreamBuilder WithMessage(Message message)
        {
            _parts.Add(stream => FrameCodec.WriteAsync(stream, message, ct: Ct));
            return this;
        }

        /// <summary>Appends only a length prefix, as a peer announcing a frame of that size would.</summary>
        public FrameStreamBuilder WithLengthPrefix(int length)
        {
            _parts.Add(stream => WriteBytes(stream, LengthPrefix(length)));
            return this;
        }

        /// <summary>Appends a well-formed length prefix followed by the given text as the payload.</summary>
        public FrameStreamBuilder WithRawPayload(string payloadText)
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(payloadText);
            _parts.Add(stream => WriteBytes(stream, [.. LengthPrefix(payload.Length), .. payload]));
            return this;
        }

        /// <summary>Cuts the last bytes off the finished stream, as a dropped connection would.</summary>
        public FrameStreamBuilder WithoutLastBytes(int count)
        {
            _bytesToRemove = count;
            return this;
        }

        /// <summary>Writes all parts and returns the stream positioned at its start.</summary>
        public async Task<MemoryStream> BuildAsync()
        {
            var stream = new MemoryStream();
            foreach (var part in _parts)
            {
                await part(stream);
            }

            stream.SetLength(stream.Length - _bytesToRemove);
            stream.Position = 0;
            return stream;
        }

        private static byte[] LengthPrefix(int length)
        {
            var prefix = new byte[FrameCodec.HeaderSize];
            BinaryPrimitives.WriteInt32BigEndian(prefix, length);
            return prefix;
        }

        private static ValueTask WriteBytes(MemoryStream stream, byte[] bytes) => stream.WriteAsync(bytes, Ct);
    }
}
