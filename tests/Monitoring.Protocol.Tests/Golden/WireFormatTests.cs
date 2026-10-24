using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Monitoring.Protocol.Framing;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Protocol.Serialization;
using Shouldly;
using Xunit;

namespace Monitoring.Protocol.Tests.Golden;

/// <summary>
/// The bytes on the wire are a contract between components that are rolled out one by one, so a frame of each message is kept next to the tests (the <c>*.json</c> files here)
/// and what is sent has to stay the same as it. A change of the wire format is allowed, but it has to be made on purpose: run
/// <c>UPDATE_GOLDEN=1 dotnet test --project tests/Monitoring.Protocol.Tests</c>, read the diff, and think about what a peer one version away does with it.
/// </summary>
public class WireFormatTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly InstanceInfo Pod = new(
        "orders-7d9f-x1", "orders", InstanceLifecycle.Running, HealthState.Degraded,
        new Dictionary<string, string> { ["app"] = "orders", ["k8s.pod-ips"] = "10.0.0.5" },
        Version: "1.2.3",
        Checks: [new HealthCheckResult("database", HealthState.Degraded, "answers in 850 ms")],
        Metrics: [new MetricSeries("http.server.request.duration", new Dictionary<string, string> { ["route"] = "/orders" }, 12.5)],
        Containers: [new ContainerInfo("app", ContainerRole.App, ContainerPhase.Running, HealthState.Healthy, Ready: true, Restarts: 2, Image: "orders:1.2.3", LastExitCode: 137, LastReason: "OOMKilled")],
        Edges: [new EdgeReport("HTTP", "http://payments:8080", 1200, 3, 16800.5, [700, 400, 100])],
        Packages: [new PackageReport("Serilog", "4.0.0", true)]);

    /// <summary>One message of each kind, with every field that matters filled in.</summary>
    public static TheoryData<string> Names => new(Samples.Keys);

    private static readonly Dictionary<string, Message> Samples = new()
    {
        ["hello"] = new Hello(2, "orders-7d9f-x1", "orders", "1.2.3", new Dictionary<string, string> { ["environment"] = "prod" }, "token", "shop"),
        ["heartbeat"] = new Heartbeat(42, HealthState.Degraded, "v3"),
        ["pull"] = new Pull(7, PullKind.Metrics, 1000),
        ["pull-result"] = new PullResult(7, PullKind.Health, 1001, """{"health":"Healthy"}"""),
        ["snapshot"] = new Snapshot(1, "shop", [Pod], new Dictionary<string, string> { ["environment"] = "prod", ["datacenter"] = "a" }),
        ["delta"] = new Delta(2, [Pod], ["orders-old"]),
        ["goodbye"] = new Goodbye("orders-7d9f-x1"),
        ["config"] = new Config("abc123", ["monitoring-controller-0:9440", "monitoring-controller-1:9440"], 5, 15, 10,
            new Dictionary<string, ServiceConfig> { ["reports"] = new(60, 30) }, ["PullSeconds"]),
        ["ack"] = new Ack(5, SnapshotRequired: true),
        ["history-request"] = new HistoryRequest(9, "orders-7d9f-x1"),
        ["history-result"] = new HistoryResult(9, """{"series":[]}"""),
    };

    private static string Wire(Message message) =>
        Indent(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJsonContext.Default.Message)));

    private static string Indent(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n");
    }

    private static string GoldenPath(string name, [CallerFilePath] string file = "") => Path.Combine(Path.GetDirectoryName(file)!, $"{name}.json");

    [Fact]
    public void There_is_a_sample_of_every_kind_of_message()
    {
        // Arrange
        var kinds = typeof(Message).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Message)) && !t.IsAbstract).Select(t => t.Name).Order();

        // Act
        var covered = Samples.Values.Select(m => m.GetType().Name).Order();

        // Assert: a new message needs a sample, and with it a frame in this folder
        covered.ShouldBe(kinds);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task What_is_sent_is_what_the_frame_of_that_message_says(string name)
    {
        // Arrange
        var actual = Wire(Samples[name]);
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(GoldenPath(name), actual + "\n", Ct);
        }

        // Act
        var golden = (await File.ReadAllTextAsync(GoldenPath(name), Ct)).ReplaceLineEndings("\n").TrimEnd();

        // Assert
        actual.ShouldBe(golden, $"The wire format of '{name}' changed. If that is on purpose, run: UPDATE_GOLDEN=1 dotnet test --project tests/Monitoring.Protocol.Tests");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task The_frame_of_a_message_is_read_back_as_the_same_message(string name)
    {
        // Arrange
        var golden = (await File.ReadAllTextAsync(GoldenPath(name), Ct)).Trim();
        // Act
        var read = await ReadAsync(golden);

        // Assert
        read.ShouldNotBeNull();
        Wire(read).ShouldBe(Wire(Samples[name]));
    }

    [Fact]
    public async Task A_peer_that_sends_more_than_this_version_knows_is_still_read()
    {
        // Arrange: a later version adds a field to a message and to a part of one; an earlier one leaves out what is optional
        var later = """{"type":"hello","protocolVersion":3,"instanceId":"x","serviceName":"orders","labels":{},"somethingNew":{"a":1},"scopeName":"shop"}""";
        var earlier = """{"type":"snapshot","sequence":1,"scopeName":"shop","instances":[{"id":"a","serviceName":"orders","lifecycle":"Running","health":"Healthy","labels":{}}]}""";

        // Act
        var hello = await ReadAsync(later);
        var snapshot = await ReadAsync(earlier);

        // Assert
        hello.ShouldBeOfType<Hello>().ScopeName.ShouldBe("shop");
        snapshot.ShouldBeOfType<Snapshot>().Instances.Single().Version.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"type":"somethingFromTheFuture","x":1}""")]
    [InlineData("""{"x":1}""")]
    [InlineData("not json")]
    public async Task A_message_of_a_kind_this_version_does_not_know_is_refused_as_a_bad_frame_and_does_not_crash_the_reader(string json)
    {
        // Act
        var read = async () => await ReadAsync(json);

        // Assert
        await read.ShouldThrowAsync<FrameException>();
    }

    private static async Task<Message?> ReadAsync(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        using var frame = new MemoryStream();
        frame.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(payload.Length)));
        frame.Write(payload);
        frame.Position = 0;
        return await FrameCodec.ReadAsync(frame, ct: Ct);
    }
}
