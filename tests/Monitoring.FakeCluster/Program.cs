// A stand-in for the Kubernetes API server of one namespace, for the environment that runs the whole system on a developer machine or in a test.
// It answers what the agent asks (the pods, and empty lists of everything else), keeps watch connections open and has a control API to change the pods:
//   PUT    /control/pods/{name}?service=orders&ready=true   adds a pod, or changes it
//   DELETE /control/pods/{name}                              removes a pod
// Pods to start with: FAKECLUSTER_PODS=orders-1:orders,payments-1:payments
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
var cluster = new Cluster();
foreach (var entry in (builder.Configuration["FAKECLUSTER_PODS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    var parts = entry.Split(':');
    cluster.Set(parts[0], parts.Length > 1 ? parts[1] : parts[0], ready: true);
}

app.MapGet("/api/v1/namespaces/{ns}/pods", async (HttpContext context, string ns) =>
{
    if (context.Request.Query["watch"] != "true")
    {
        return Results.Text($"{{\"metadata\":{{\"resourceVersion\":\"1\"}},\"items\":[{string.Join(",", cluster.Pods())}]}}", "application/json");
    }

    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("\n");
    await context.Response.Body.FlushAsync();
    var events = cluster.Watch();
    try
    {
        await foreach (var line in events.Reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync(line + "\n");
            await context.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // The agent ended the watch.
    }
    finally
    {
        cluster.Unwatch(events);
    }

    return Results.Empty;
});

app.MapGet("/api/v1/namespaces/{ns}/events", (string ns) => Results.Text("{\"items\":[]}", "application/json"));
app.MapGet("/api/v1/namespaces/{ns}/services", (string ns) => Results.Text("{\"items\":[]}", "application/json"));
app.MapGet("/apis/apps/v1/namespaces/{ns}/{kind}", (string ns, string kind) => Results.Text("{\"items\":[]}", "application/json"));

app.MapPut("/control/pods/{name}", (string name, string? service, bool? ready) =>
{
    cluster.Set(name, service ?? name, ready ?? true);
    return Results.NoContent();
});
app.MapDelete("/control/pods/{name}", (string name) => cluster.Delete(name) ? Results.NoContent() : Results.NotFound());
app.MapGet("/healthz", () => "ok");

app.Run();

/// <summary>The pods of the namespace, and the watchers that are told when they change.</summary>
internal sealed class Cluster
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Service, bool Ready)> _pods = [];
    private readonly List<Channel<string>> _watchers = [];

    public void Set(string name, string service, bool ready)
    {
        lock (_gate)
        {
            var existed = _pods.ContainsKey(name);
            _pods[name] = (service, ready);
            Push(existed ? "MODIFIED" : "ADDED", PodJson(name, service, ready));
        }
    }

    public bool Delete(string name)
    {
        lock (_gate)
        {
            if (!_pods.Remove(name, out var pod))
            {
                return false;
            }

            Push("DELETED", PodJson(name, pod.Service, pod.Ready));
            return true;
        }
    }

    public List<string> Pods()
    {
        lock (_gate)
        {
            return [.. _pods.Select(pod => PodJson(pod.Key, pod.Value.Service, pod.Value.Ready))];
        }
    }

    public Channel<string> Watch()
    {
        var channel = Channel.CreateUnbounded<string>();
        lock (_gate)
        {
            _watchers.Add(channel);
        }

        return channel;
    }

    public void Unwatch(Channel<string> channel)
    {
        lock (_gate)
        {
            _watchers.Remove(channel);
        }
    }

    private void Push(string type, string podJson)
    {
        var line = "{\"type\":\"" + type + "\",\"object\":" + podJson + "}";
        foreach (var watcher in _watchers)
        {
            watcher.Writer.TryWrite(line);
        }
    }

    private static string PodJson(string name, string service, bool ready) =>
        JsonSerializer.Serialize(new
        {
            metadata = new
            {
                name,
                uid = $"uid-{name}",
                resourceVersion = "5",
                labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = service },
            },
            spec = new { nodeName = "node-1", containers = new[] { new { name = "app", image = $"{service}:1" }, new { name = "monitoring-sidecar", image = "monitoring-sidecar:1" } } },
            status = new
            {
                phase = "Running",
                podIP = "127.0.0.1",
                conditions = new[] { new { type = "Ready", status = ready ? "True" : "False" } },
                containerStatuses = new object[]
                {
                    new { name = "app", ready, restartCount = 0, state = new { running = new { } } },
                    new { name = "monitoring-sidecar", ready = true, restartCount = 0, state = new { running = new { } } },
                },
            },
        });
}
