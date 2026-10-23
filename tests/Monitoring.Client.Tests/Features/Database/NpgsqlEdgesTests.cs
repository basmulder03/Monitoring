using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Tracing;
using Npgsql;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Database;

/// <summary>
/// A real database provider, to see what it publishes and that it becomes a database edge. It needs a PostgreSQL server and says so when there is none:
/// set <c>MONITORING_TEST_POSTGRES</c> to a connection string, for example <c>Host=127.0.0.1;Port=54329;Username=postgres;Password=...</c>.
/// </summary>
[Collection("Meters")]
public class NpgsqlEdgesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? ConnectionString => Environment.GetEnvironmentVariable("MONITORING_TEST_POSTGRES");

    [Fact]
    public async Task Queries_of_npgsql_become_edges_to_the_database_when_its_source_is_listened_to()
    {
        Assert.SkipUnless(ConnectionString is not null, "Set MONITORING_TEST_POSTGRES to a PostgreSQL connection string to run this.");

        // Arrange
        var options = new MonitoringOptions { TraceSources = ["Npgsql"] };
        var edges = new EdgeCounters(options.MaxEdges);
        var spans = new SpanBuffer(options.SpanBufferSize);
        using var collector = new TraceCollector(options, edges, spans);
        await collector.StartAsync(Ct);
        await using var data = new NpgsqlDataSourceBuilder(ConnectionString).Build();

        // Act
        await using (var command = data.CreateCommand("select 1"))
        {
            await command.ExecuteScalarAsync(Ct);
        }

        var broken = async () =>
        {
            await using var command = data.CreateCommand("select * from table_that_does_not_exist");
            await command.ExecuteScalarAsync(Ct);
        };
        await broken.ShouldThrowAsync<PostgresException>();

        // Assert
        var edge = edges.Read().Edges.Single(e => e.Type == "DB");
        // Npgsql leaves the port out when it is the default one.
        edge.Target.ShouldStartWith("postgresql://127.0.0.1");
        edge.Target.ShouldEndWith("/postgres");
        edge.Calls.ShouldBe(2);
        edge.Errors.ShouldBe(1);
        var details = spans.ReadAfter(0).Spans.Select(span => string.Join(", ", span.Details.Select(d => $"{d.Key}={d.Value}"))).ToList();
        details.ShouldContain(text => text.Contains("db.statement=select ?", StringComparison.Ordinal));
        spans.ReadAfter(0).Spans.Select(span => span.Name).ShouldBe(["postgresql select", "postgresql select"]);
        spans.ReadAfter(0).Spans.Select(span => span.Error).ShouldBe([false, true]);
    }
}
