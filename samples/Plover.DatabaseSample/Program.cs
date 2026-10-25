// A spike: does a real database provider, published as Native AOT, become a database edge? It asks PostgreSQL a question and a wrong one, and then asks
// its own Plover client, over the socket a sidecar would use, what it says about the calls. Run it with PLOVER_SAMPLE_POSTGRES set.
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plover.Protocol.Framing;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);
var socket = Path.Combine(Path.GetTempPath(), $"plover-sample-{Guid.NewGuid():N}.sock");
builder.Services.AddPlover(options =>
{
    options.SocketPath = socket;
    options.AgentPipe = null;

    // The one line a database needs: the name of the activity source of its provider.
    options.TraceSources.Add("Npgsql");
});
using var host = builder.Build();
await host.StartAsync();

await using var data = new NpgsqlDataSourceBuilder(Environment.GetEnvironmentVariable("PLOVER_SAMPLE_POSTGRES")).Build();
await using (var good = data.CreateCommand("select 1 + 1"))
{
    Console.WriteLine($"answer: {await good.ExecuteScalarAsync()}");
}

try
{
    await using var bad = data.CreateCommand("select * from table_that_does_not_exist");
    await bad.ExecuteScalarAsync();
}
catch (PostgresException ex)
{
    Console.WriteLine($"failed as expected: {ex.SqlState}");
}

// What a sidecar would be told.
using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
for (var i = 0; i < 100 && !File.Exists(socket); i++)
{
    await Task.Delay(20);
}

await client.ConnectAsync(new UnixDomainSocketEndPoint(socket));
using var stream = new NetworkStream(client);
await FrameCodec.WriteAsync(stream, new Pull(1, PullKind.Edges, 0));
var edges = ReportPayload.ReadEdges(((PullResult)(await FrameCodec.ReadAsync(stream))!).PayloadJson);
foreach (var edge in edges!.Edges)
{
    Console.WriteLine($"edge: {edge.Type} {edge.Target} calls={edge.Calls} errors={edge.Errors}");
}

await FrameCodec.WriteAsync(stream, new Pull(2, PullKind.Spans, 0));
var spans = ReportPayload.ReadSpans(((PullResult)(await FrameCodec.ReadAsync(stream))!).PayloadJson);
foreach (var span in spans!.Spans)
{
    Console.WriteLine($"span: {span.Name} error={span.Error} {string.Join(", ", span.Details.Select(d => $"{d.Key}={d.Value}"))}");
}

await host.StopAsync();
