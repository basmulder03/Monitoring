using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Monitoring.Client.Tests.Features.Tracing;

/// <summary>A server that answers every request with "ok" and remembers the trace header it was sent.</summary>
internal sealed class TinyHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public TinyHttpServer()
    {
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string Address => $"http://127.0.0.1:{Port}";

    public string? LastTraceParent { get; private set; }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var read = await stream.ReadAsync(buffer, _stop.Token);
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                LastTraceParent = request.Split("\r\n").FirstOrDefault(l => l.StartsWith("traceparent:", StringComparison.OrdinalIgnoreCase));
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), _stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
        {
            // Stopped.
        }
    }
}
