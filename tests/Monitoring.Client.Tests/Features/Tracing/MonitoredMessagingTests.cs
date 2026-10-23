using System.Diagnostics;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Tracing;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Tracing;

[Collection("Meters")]
public class MonitoredMessagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(TraceCollector Collector, EdgeCounters Edges, SpanBuffer Spans)> StartAsync()
    {
        var options = new MonitoringOptions().UseActiveMq();
        var edges = new EdgeCounters(options.MaxEdges);
        var spans = new SpanBuffer(options.SpanBufferSize);
        var collector = new TraceCollector(options, edges, spans);
        await collector.StartAsync(Ct);
        return (collector, edges, spans);
    }

    [Fact]
    public async Task A_sent_message_is_counted_as_a_call_to_its_queue_and_carries_the_trace_context()
    {
        // Arrange
        var (collector, edges, _) = await StartAsync();
        using (collector)
        {
            var properties = new Dictionary<string, string>();
            string expected;

            // Act
            using (var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "orders", properties.Add))
            {
                expected = send!.Id!;
            }

            // Assert
            properties[MonitoredMessaging.TraceParent].ShouldBe(expected);
            var edge = edges.Read().Edges.Single(e => e.Target == "activemq://orders");
            edge.Type.ShouldBe("MQ");
            edge.Calls.ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_message_that_is_taken_continues_the_trace_of_its_sender_and_is_counted_as_taken_from_the_queue()
    {
        // Arrange
        var (collector, edges, spans) = await StartAsync();
        using (collector)
        {
            var properties = new Dictionary<string, string>();
            string traceId;
            using (var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "orders", properties.Add))
            {
                traceId = send!.TraceId.ToHexString();
            }

            // Act
            using (MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, "orders", name => properties.GetValueOrDefault(name)))
            {
            }

            // Assert
            edges.Read().Edges.Single(e => e.Type == "MQ-IN").Target.ShouldBe("activemq://orders");
            var processed = spans.ReadAfter(0).Spans.Single(s => s.Kind == "Consumer");
            processed.TraceId.ShouldBe(traceId);
            processed.ParentId.ShouldNotBeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a trace context")]
    public async Task A_message_without_a_usable_trace_context_starts_a_trace_of_its_own(string? traceparent)
    {
        // Arrange
        var (collector, edges, _) = await StartAsync();
        using (collector)
        {
            // Act
            using (MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, "orders", _ => traceparent))
            {
            }

            // Assert
            edges.Read().Edges.Single().Type.ShouldBe("MQ-IN");
        }
    }

    [Fact]
    public async Task A_failed_send_is_counted_as_an_error()
    {
        // Arrange
        var (collector, edges, _) = await StartAsync();
        using (collector)
        {
            // Act
            using (var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "orders", (_, _) => { }))
            {
                send.Fail(new IOException("broker gone"));
            }

            // Assert
            edges.Read().Edges.Single().Errors.ShouldBe(1);
        }
    }

    [Fact]
    public void Without_monitoring_nothing_is_set_and_nothing_is_started()
    {
        // Arrange
        var properties = new Dictionary<string, string>();

        // Act
        var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "orders", properties.Add);
        var process = MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, "orders", _ => null);

        // Assert
        send.ShouldBeNull();
        process.ShouldBeNull();
        properties.ShouldBeEmpty();
    }

    [Fact]
    public void UseActiveMq_adds_the_source_once()
    {
        // Act
        var options = new MonitoringOptions().UseActiveMq().UseActiveMq();

        // Assert
        options.TraceSources.Count(s => s == MonitoredMessaging.SourceName).ShouldBe(1);
    }
}
