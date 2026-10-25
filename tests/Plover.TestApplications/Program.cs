// The applications of the end-to-end environment, one program in three roles (ROLE=api|worker|data), so the call chain is known exactly:
//   api    GET /order   puts a message on the queue "orders"
//   worker takes the message from the queue and asks the data service
//   data   GET /store   asks the database a question
// All three use the Plover client; the sidecar next to each tells the agent what it sees.
using ActiveMQ.Artemis.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Plover.Client.Features.Tracing;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var role = builder.Configuration["ROLE"] ?? "api";
var queue = "orders";

builder.Services.AddPlover(options =>
{
    options.ServiceName = builder.Configuration["SERVICE_NAME"] ?? role;
    options.Version = "1.0.0";
    options.UseActiveMq();
    options.TraceSources.Add("Npgsql");
});

var brokenFile = builder.Configuration["BROKEN_FILE"] ?? "/tmp/broken";
builder.Services.AddHealthChecks().AddCheck("switch", () =>
    File.Exists(brokenFile) ? HealthCheckResult.Unhealthy("switched off") : HealthCheckResult.Healthy());

if (role == "worker")
{
    builder.Services.AddHostedService(_ => new Worker(Setting(builder, "ARTEMIS"), queue, Setting(builder, "DATA_URL")));
}

var app = builder.Build();
app.MapGet("/", () => role);

if (role == "api")
{
    var artemis = Setting(builder, "ARTEMIS");
    IProducer? producer = null;
    var gate = new SemaphoreSlim(1);
    app.MapGet("/order", async () =>
    {
        await gate.WaitAsync();
        try
        {
            producer ??= await (await Broker.ConnectAsync(artemis)).CreateProducerAsync(queue, RoutingType.Anycast);
        }
        finally
        {
            gate.Release();
        }

        var message = new Message("an order");
        using var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, queue, (name, value) => message.ApplicationProperties[name] = value);
        try
        {
            await producer.SendAsync(message);
        }
        catch (Exception ex)
        {
            send.Fail(ex);
            throw;
        }

        return "sent";
    });
}

if (role == "data")
{
    var data = new NpgsqlDataSourceBuilder(Setting(builder, "POSTGRES")).Build();
    app.MapGet("/store", async () =>
    {
        await using var command = data.CreateCommand("select 1");
        return (await command.ExecuteScalarAsync())?.ToString();
    });
}

app.Run();

static string Setting(WebApplicationBuilder builder, string name) =>
    builder.Configuration[name] ?? throw new InvalidOperationException($"{name} is not set.");

internal static class Broker
{
    /// <summary>Connects to the broker, which may still be starting: tries for a minute.</summary>
    public static async Task<IConnection> ConnectAsync(string setting, CancellationToken ct = default)
    {
        var parts = setting.Split(':');
        var endpoint = ActiveMQ.Artemis.Client.Endpoint.Create(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[2], parts[3]);
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (true)
        {
            try
            {
                return await new ConnectionFactory().CreateAsync(endpoint, ct);
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000, ct);
            }
        }
    }
}

/// <summary>Takes the messages of the queue and, for each, asks the data service.</summary>
internal sealed class Worker(string artemis, string queue, string dataUrl) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await using var connection = await Broker.ConnectAsync(artemis, stoppingToken);
        await using var consumer = await connection.CreateConsumerAsync(queue, RoutingType.Anycast, stoppingToken);
        using var http = new HttpClient();
        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await consumer.ReceiveAsync(stoppingToken);
            using var process = MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, queue, name => message.ApplicationProperties.TryGetValue<string>(name, out var value) ? value : null);
            try
            {
                using var response = await http.GetAsync($"{dataUrl}/store", stoppingToken);
                response.EnsureSuccessStatusCode();
                await consumer.AcceptAsync(message);
            }
            catch (Exception ex) when (ex is HttpRequestException)
            {
                process.Fail(ex);
                consumer.Reject(message);
            }
        }
    }
}
