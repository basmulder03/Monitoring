// A stand-in for the Kubernetes API server of one namespace, for the environment that runs the whole system on a developer machine or in a test.
// It answers what the agent asks (the pods, and empty lists of everything else), keeps watch connections open and has a control API to change the pods:
//   PUT    /control/pods/{name}?service=orders&ready=true&fault=crash   adds a pod, or changes it; fault is crash (the application keeps being killed for lack of memory) or sidecar (the Plover sidecar crash loops)
//   DELETE /control/pods/{name}                              removes a pod
//   POST   /receive/webhook   keeps what an alert webhook sends; GET lists it as a JSON array, DELETE forgets it
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
    cluster.Set(parts[0], parts.Length > 1 ? parts[1] : parts[0], ready: true, fault: null);
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

app.MapPut("/control/pods/{name}", (string name, string? service, bool? ready, string? fault) =>
{
    cluster.Set(name, service ?? name, ready ?? true, fault);
    return Results.NoContent();
});
app.MapDelete("/control/pods/{name}", (string name) => cluster.Delete(name) ? Results.NoContent() : Results.NotFound());
var received = new ConcurrentQueue<string>();
app.MapPost("/receive/webhook", async (HttpRequest request) =>
{
    received.Enqueue(await new StreamReader(request.Body).ReadToEndAsync());
    return Results.Ok();
});
app.MapGet("/receive/webhook", () => Results.Text($"[{string.Join(",", received)}]", "application/json"));
app.MapDelete("/receive/webhook", () =>
{
    received.Clear();
    return Results.NoContent();
});
app.MapGet("/healthz", () => "ok");

app.Run();

/// <summary>The pods of the namespace, and the watchers that are told when they change.</summary>
internal sealed class Cluster
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Service, bool Ready, string? Fault)> _pods = [];
    private readonly List<Channel<string>> _watchers = [];

    public void Set(string name, string service, bool ready, string? fault)
    {
        lock (_gate)
        {
            var existed = _pods.ContainsKey(name);
            _pods[name] = (service, ready, fault);
            Push(existed ? "MODIFIED" : "ADDED", PodJson(name, service, ready, fault));
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

            Push("DELETED", PodJson(name, pod.Service, pod.Ready, pod.Fault));
            return true;
        }
    }

    public List<string> Pods()
    {
        lock (_gate)
        {
            return [.. _pods.Select(pod => PodJson(pod.Key, pod.Value.Service, pod.Value.Ready, pod.Value.Fault))];
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

    private static object Running => new { running = new { } };

    private static object CrashLoop => new { waiting = new { reason = "CrashLoopBackOff", message = "back-off 5m0s restarting failed container" } };

    private static string PodJson(string name, string service, bool ready, string? fault) =>
        JsonSerializer.Serialize(new
        {
            metadata = new
            {
                name,
                uid = $"uid-{name}",
                resourceVersion = "5",
                labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = service },
            },
            spec = new { nodeName = "node-1", containers = new[] { new { name = "app", image = $"{service}:1" }, new { name = "plover-sidecar", image = "plover-sidecar:1" } } },
            status = new
            {
                phase = "Running",
                podIP = "127.0.0.1",
                conditions = new[] { new { type = "Ready", status = ready ? "True" : "False" } },
                containerStatuses = new object[]
                {
                    fault == "crash"
                        ? new { name = "app", ready = false, restartCount = 7, state = CrashLoop, lastState = (object)new { terminated = new { reason = "OOMKilled", exitCode = 137 } } }
                        : new { name = "app", ready, restartCount = 0, state = Running, lastState = (object)new { } },
                    fault == "sidecar"
                        ? new { name = "plover-sidecar", ready = false, restartCount = 7, state = CrashLoop, lastState = (object)new { terminated = new { reason = "Error", exitCode = 1 } } }
                        : new { name = "plover-sidecar", ready = true, restartCount = 0, state = Running, lastState = (object)new { } },
                },
            },
        });
}
