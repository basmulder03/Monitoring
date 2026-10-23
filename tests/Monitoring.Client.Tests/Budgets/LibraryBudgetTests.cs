using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Tracing;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Budgets;

/// <summary>
/// What the client library keeps in the memory of the application when every buffer is full: the most dependencies it counts, the most spans it keeps and the most series it holds. These are the
/// limits the options set by default, so the budget is the cost of the library in the worst case, not a typical one. The limit is well above what was measured.
/// </summary>
[Collection("Meters")]
public class LibraryBudgetTests(ITestOutputHelper output)
{
    private const long LimitBytes = 3 * 1024 * 1024;

    [Fact]
    public void A_full_set_of_buffers_costs_the_application_less_than_three_megabytes()
    {
        // Arrange
        var options = new MonitoringOptions();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        // Act: more of everything than the limits allow, so the limits are what is measured
        var edges = new EdgeCounters(options.MaxEdges);
        for (var target = 0; target < options.MaxEdges * 2; target++)
        {
            edges.Record("HTTP", $"http://service-{target}.shop.svc:8080", target % 50, error: target % 7 == 0);
        }

        var spans = new SpanBuffer(options.SpanBufferSize);
        var details = new Dictionary<string, string> { ["http.method"] = "GET", ["http.url"] = "http://service-1.shop.svc:8080/orders/?", ["db.statement"] = "select * from orders where id = ?" };
        for (var span = 0; span < options.SpanBufferSize * 2; span++)
        {
            spans.Add(sequence => new SpanReport(sequence, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")[..16], null, "GET /orders", "Client", DateTimeOffset.UtcNow, 12.5, false, details));
        }

        var held = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(edges);
        GC.KeepAlive(spans);

        // Assert
        output.WriteLine($"Edges and spans at their limits: {held / 1024.0 / 1024:F1} MB");
        edges.Count.ShouldBe(options.MaxEdges);
        spans.Count.ShouldBe(options.SpanBufferSize);
        held.ShouldBeLessThan(LimitBytes);
    }
}
