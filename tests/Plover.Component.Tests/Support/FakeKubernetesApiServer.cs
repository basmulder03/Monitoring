using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Plover.Component.Tests.Support;

/// <summary>
/// A stand-in for the Kubernetes API server that serves the pod list of one namespace and keeps watch connections open,
/// so a test can change the pods while an agent is following them.
/// </summary>
internal sealed class FakeKubernetesApiServer : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _pods = [];
    private readonly List<StreamWriter> _watchers = [];
    private readonly CancellationTokenSource _stop = new();
    private HttpListener? _listener;
    private Task? _serving;

    /// <summary>The address agents are pointed at.</summary>
    public string Address { get; private set; } = "";

    /// <summary>The Authorization header of the most recent request, or null when it had none.</summary>
    public string? LastAuthorization { get; private set; }

    /// <summary>How many watch connections are open right now.</summary>
    public int OpenWatches
    {
        get
        {
            lock (_gate)
            {
                return _watchers.Count;
            }
        }
    }

    /// <summary>Starts serving on a free loopback port.</summary>
    /// <returns>This server, for chaining.</returns>
    public FakeKubernetesApiServer Start()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Address = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"{Address}/");
        _listener.Start();
        _serving = Task.Run(ServeAsync);
        return this;
    }

    /// <summary>Adds a pod to the list the server answers with.</summary>
    /// <param name="name">Pod name.</param>
    /// <param name="ready">Whether the pod is running and ready.</param>
    /// <returns>This server, for chaining.</returns>
    public FakeKubernetesApiServer WithPod(string name, bool ready = true, string ip = LoopbackIp)
    {
        lock (_gate)
        {
            _ips[name] = ip;
            _pods.Add(PodJson(name, ready));
        }

        return this;
    }

    /// <summary>Tells every open watch that a pod was deleted.</summary>
    /// <param name="name">Pod name.</param>
    public void DeletePod(string name) => Push("DELETED", PodJson(name, ready: true));

    /// <summary>Tells every open watch that a pod changed.</summary>
    /// <param name="name">Pod name.</param>
    /// <param name="ready">Whether the pod is now ready.</param>
    public void ChangePod(string name, bool ready) => Push("MODIFIED", PodJson(name, ready));

    /// <summary>Tells every open watch that a pod appeared.</summary>
    /// <param name="name">Pod name.</param>
    public void AddPod(string name) => Push("ADDED", PodJson(name, ready: true));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener?.Abort();
        if (_serving is not null)
        {
            await _serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>The sidecars of the tests connect from the loopback address, so that is the address the pods have unless a test says another.</summary>
    private const string LoopbackIp = "127.0.0.1";

    private readonly Dictionary<string, string> _ips = [];

    private string PodJson(string name, bool ready) =>
        JsonSerializer.Serialize(new
        {
            metadata = new
            {
                name,
                uid = $"uid-{name}",
                resourceVersion = "5",
                labels = new Dictionary<string, string> { ["app.kubernetes.io/name"] = "orders" },
            },
            spec = new { nodeName = "node-1", containers = new[] { new { name = "app", image = "orders:1" } } },
            status = new
            {
                phase = "Running",
                podIP = _ips.GetValueOrDefault(name, LoopbackIp),
                conditions = new[] { new { type = "Ready", status = ready ? "True" : "False" } },
                containerStatuses = new[] { new { name = "app", ready, restartCount = 0, state = new { running = new { } } } },
            },
        });

    private void Push(string type, string podJson)
    {
        var line = "{\"type\":\"" + type + "\",\"object\":" + podJson + "}";
        lock (_gate)
        {
            foreach (var watcher in _watchers.ToList())
            {
                try
                {
                    watcher.WriteLine(line);
                    watcher.Flush();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or HttpListenerException)
                {
                    _watchers.Remove(watcher);
                }
            }
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener!.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        LastAuthorization = context.Request.Headers["Authorization"];
        var response = context.Response;
        try
        {
            if (context.Request.QueryString["watch"] == "true")
            {
                response.SendChunked = true;
                response.ContentType = "application/json";
                var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false)) { AutoFlush = false };
                writer.WriteLine();
                writer.Flush();
                lock (_gate)
                {
                    _watchers.Add(writer);
                }

                // Hold the connection open until the server stops; the client ends it by disconnecting.
                await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                return;
            }

            string body;
            lock (_gate)
            {
                body = "{\"metadata\":{\"resourceVersion\":\"1\"},\"items\":[" + string.Join(",", _pods) + "]}";
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException or ObjectDisposedException)
        {
            // The client went away or the server is stopping.
        }
    }
}
