using System.Diagnostics;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Tracing;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Tracing;

public class TraceCollectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_finished_http_call_is_counted_as_an_edge()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
            a.SetTag("server.port", 8080);
            a.SetTag("http.response.status_code", 200);
        });

        // Assert
        var edge = fixture.Edges.Read().Edges.Single();
        edge.Type.ShouldBe("HTTP");
        edge.Target.ShouldBe("shop:8080");
        edge.Calls.ShouldBe(1);
        edge.Errors.ShouldBe(0);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_call_that_ends_in_a_server_error_counts_as_failed(int status)
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
            a.SetTag("http.response.status_code", status);
        });

        // Assert
        fixture.Edges.Read().Edges.Single().Errors.ShouldBe(1);
    }

    [Fact]
    public async Task A_call_with_an_error_status_or_error_type_counts_as_failed()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
            a.SetStatus(ActivityStatusCode.Error);
        });
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
            a.SetTag("error.type", "System.Net.Http.HttpRequestException");
        });

        // Assert
        fixture.Edges.Read().Edges.Single().Errors.ShouldBe(2);
    }

    [Fact]
    public async Task A_database_call_is_an_edge_to_the_server_and_database()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("db.system", "mssql");
            a.SetTag("server.address", "db1");
            a.SetTag("server.port", 1433);
            a.SetTag("db.namespace", "orders");
        });

        // Assert
        var edge = fixture.Edges.Read().Edges.Single();
        edge.Type.ShouldBe("DB");
        edge.Target.ShouldBe("mssql://db1:1433/orders");
    }

    [Fact]
    public async Task A_message_sent_to_a_queue_is_an_edge_to_the_queue()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Producer, a =>
        {
            a.SetTag("messaging.system", "activemq");
            a.SetTag("messaging.destination.name", "orders");
        });

        // Assert
        var edge = fixture.Edges.Read().Edges.Single();
        edge.Type.ShouldBe("MQ");
        edge.Target.ShouldBe("activemq://orders");
    }

    [Fact]
    public async Task An_incoming_request_is_a_span_but_not_an_edge()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Server, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "me");
        });

        // Assert
        fixture.Edges.Read().Edges.ShouldBeEmpty();
        fixture.Spans.ReadAfter(0).Spans.Single().Kind.ShouldBe("Server");
    }

    [Fact]
    public async Task A_span_carries_ids_timing_and_cleaned_details()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
            a.SetTag("url.full", "https://shop/orders/7?token=secret");
            a.SetTag("db.query.text", "SELECT * FROM t WHERE name = 'Bob'");
            a.SetTag("password", "never");
        });

        // Assert
        var span = fixture.Spans.ReadAfter(0).Spans.Single();
        span.TraceId.Length.ShouldBe(32);
        span.SpanId.Length.ShouldBe(16);
        span.Details["url"].ShouldBe("https://shop/orders/7");
        span.Details["db.statement"].ShouldBe("SELECT * FROM t WHERE name = ?");
        span.Details.ShouldNotContainKey("password");
        span.Details["http.request.method"].ShouldBe("GET");
    }

    [Fact]
    public async Task The_sources_that_are_not_listed_cost_nothing_and_leave_no_trace()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        using var other = new ActivitySource($"other-{Guid.NewGuid():N}");

        // Act
        using var activity = other.StartActivity("x", ActivityKind.Client);

        // Assert
        activity.ShouldBeNull();
        fixture.Spans.ReadAfter(0).Spans.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_recorded_when_monitoring_is_turned_off()
    {
        // Arrange
        using var fixture = await new CollectorFixture().Disabled().StartAsync();

        // Act
        fixture.Run(ActivityKind.Client, a => a.SetTag("server.address", "shop"));

        // Assert
        fixture.Spans.ReadAfter(0).Spans.ShouldBeEmpty();
    }

    [Fact]
    public async Task Real_http_calls_of_the_application_are_counted_with_a_trace_header_sent()
    {
        // Arrange
        using var server = new TinyHttpServer();
        var options = new MonitoringOptions { Enabled = true };
        var edges = new EdgeCounters(10);
        var spans = new SpanBuffer(100);
        using var collector = new TraceCollector(options, edges, spans);
        await collector.StartAsync(Ct);
        using var client = new HttpClient();

        // Act
        await client.GetStringAsync($"{server.Address}/orders?token=secret", Ct);

        // Assert
        var edge = edges.Read().Edges.Single(e => e.Target == $"127.0.0.1:{server.Port}");
        edge.Type.ShouldBe("HTTP");
        edge.Calls.ShouldBe(1);
        server.LastTraceParent.ShouldNotBeNull();
        spans.ReadAfter(0).Spans.Single(s => s.Kind == "Client").Details.ShouldNotContainKey("token");
    }

    [Fact]
    public async Task The_edges_and_spans_pull_sources_answer_with_the_collected_data_and_a_cursor()
    {
        // Arrange
        using var fixture = await new CollectorFixture().StartAsync();
        fixture.Run(ActivityKind.Client, a =>
        {
            a.SetTag("http.request.method", "GET");
            a.SetTag("server.address", "shop");
        });

        // Act
        var edges = await new EdgesPullSource(fixture.Edges).ReadAsync(7, Ct);
        var first = await new SpansPullSource(fixture.Spans).ReadAsync(0, Ct);
        var second = await new SpansPullSource(fixture.Spans).ReadAsync(first.Cursor, Ct);

        // Assert
        Monitoring.Protocol.Reports.ReportPayload.ReadEdges(edges.Payload)!.Edges.Single().Target.ShouldBe("shop");
        edges.Cursor.ShouldBe(7);
        Monitoring.Protocol.Reports.ReportPayload.ReadSpans(first.Payload)!.Spans.ShouldHaveSingleItem();
        Monitoring.Protocol.Reports.ReportPayload.ReadSpans(second.Payload)!.Spans.ShouldBeEmpty();
        second.Cursor.ShouldBe(first.Cursor);
    }

    private sealed class CollectorFixture : IDisposable
    {
        private readonly string _sourceName = $"test-{Guid.NewGuid():N}";
        private readonly ActivitySource _source;
        private readonly MonitoringOptions _options = new() { Enabled = true };
        private TraceCollector? _collector;

        public CollectorFixture()
        {
            _source = new ActivitySource(_sourceName);
            _options.TraceSources = [_sourceName];
        }

        public EdgeCounters Edges { get; } = new(100);

        public SpanBuffer Spans { get; } = new(100);

        public CollectorFixture Disabled()
        {
            _options.Enabled = false;
            return this;
        }

        public async Task<CollectorFixture> StartAsync()
        {
            _collector = new TraceCollector(_options, Edges, Spans);
            await _collector.StartAsync(Ct);
            return this;
        }

        /// <summary>Runs one operation of the given kind, letting the test set its tags before it ends. Does nothing when nobody listens, as in the application.</summary>
        public void Run(ActivityKind kind, Action<Activity> describe)
        {
            using var activity = _source.StartActivity("op", kind);
            if (activity is not null)
            {
                describe(activity);
            }
        }

        public void Dispose()
        {
            _collector?.Dispose();
            _source.Dispose();
        }
    }
}
