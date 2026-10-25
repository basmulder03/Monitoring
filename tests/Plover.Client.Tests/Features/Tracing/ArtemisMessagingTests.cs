using ActiveMQ.Artemis.Client;
using Plover.Client.Features.LocalServer;
using Plover.Client.Features.Tracing;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Features.Tracing;

/// <summary>
/// The helper around a real ActiveMQ Artemis broker: the trace context travels in a message property from the sender to the receiver. It needs a broker and says so when there is none:
/// set <c>PLOVER_TEST_ARTEMIS</c> to <c>host:port:user:password</c> of its AMQP port, for example <c>127.0.0.1:56720:art:spike-only</c>.
/// </summary>
[Collection("Meters")]
public class ArtemisMessagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Setting => Environment.GetEnvironmentVariable("PLOVER_TEST_ARTEMIS");

    [Fact]
    public async Task The_trace_context_travels_with_a_message_through_a_real_broker_and_both_sides_are_counted()
    {
        Assert.SkipUnless(Setting is not null, "Set PLOVER_TEST_ARTEMIS to host:port:user:password of an Artemis broker to run this.");

        // Arrange
        var parts = Setting!.Split(':');
        var options = new PloverOptions().UseActiveMq();
        var edges = new EdgeCounters(options.MaxEdges);
        using var collector = new TraceCollector(options, edges, new SpanBuffer(options.SpanBufferSize));
        await collector.StartAsync(Ct);
        var queue = $"orders-{Guid.NewGuid():N}";
        await using var connection = await new ConnectionFactory().CreateAsync(Endpoint.Create(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2], parts[3]), Ct);
        await using var producer = await connection.CreateProducerAsync(queue, RoutingType.Anycast, Ct);
        await using var consumer = await connection.CreateConsumerAsync(queue, RoutingType.Anycast, Ct);

        // Act
        string sentTrace;
        var message = new Message("an order");
        using (var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, queue, (name, value) => message.ApplicationProperties[name] = value))
        {
            sentTrace = send!.TraceId.ToHexString();
            await producer.SendAsync(message, Ct);
        }

        var received = await consumer.ReceiveAsync(Ct);
        string receivedTrace;
        using (var process = MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, queue, name => received.ApplicationProperties.TryGetValue<string>(name, out var value) ? value : null))
        {
            receivedTrace = process!.TraceId.ToHexString();
        }

        await consumer.AcceptAsync(received);

        // Assert
        receivedTrace.ShouldBe(sentTrace);
        var found = edges.Read().Edges.Where(e => e.Target == $"activemq://{queue}").ToDictionary(e => e.Type);
        found.Keys.Order().ShouldBe(["MQ", "MQ-IN"]);
        found.Values.ShouldAllBe(e => e.Calls == 1 && e.Errors == 0);
    }
}
