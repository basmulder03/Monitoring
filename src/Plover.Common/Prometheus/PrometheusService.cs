using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Common.Prometheus;

/// <summary>Serves <c>GET /metrics</c> in the Prometheus text format on a port of its own, using only the shared TCP listener.</summary>
/// <param name="options">The address and port.</param>
/// <param name="source">Reads the current values on every request.</param>
/// <param name="logger">Receives where it listens and faults.</param>
internal sealed class PrometheusService(PrometheusOptions options, Func<IEnumerable<PrometheusSample>> source, ILogger<PrometheusService> logger) : BackgroundService
{
    private const int MaxRequestLineBytes = 2048;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private TcpConnectionListener? _listener;

    /// <summary>Binds the port, so a port that cannot be used stops the process from starting instead of failing later.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes once the port is bound.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpConnectionListener(IPAddress.Parse(options.Address), options.Port, ServeAsync, "Prometheus scrapes", logger);
        _listener.Start();
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Gets the address the export listens on; <see langword="null"/> before the service started.</summary>
    public IPEndPoint? LocalEndPoint => _listener?.LocalEndPoint;

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _listener!.RunAsync(stoppingToken);

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        string? requestLine;
        try
        {
            requestLine = await ReadRequestLineAsync(stream, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return;
        }

        var parts = requestLine?.Split(' ');
        if (parts is not [var method, var target, ..])
        {
            await RespondAsync(stream, "400 Bad Request", "text/plain", "", ct).ConfigureAwait(false);
        }
        else if (method != "GET")
        {
            await RespondAsync(stream, "405 Method Not Allowed", "text/plain", "", ct).ConfigureAwait(false);
        }
        else if (target.Split('?')[0] != "/metrics")
        {
            await RespondAsync(stream, "404 Not Found", "text/plain", "", ct).ConfigureAwait(false);
        }
        else
        {
            await RespondAsync(stream, "200 OK", PrometheusText.ContentType, PrometheusText.Write(source()), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Reads up to the first line break; the headers after it do not matter for a scrape.</summary>
    private static async Task<string?> ReadRequestLineAsync(Stream stream, CancellationToken ct)
    {
        var line = new List<byte>();
        var one = new byte[1];
        while (line.Count < MaxRequestLineBytes && await stream.ReadAsync(one, ct).ConfigureAwait(false) == 1)
        {
            if (one[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
            }

            line.Add(one[0]);
        }

        return null;
    }

    private static async Task RespondAsync(Stream stream, string status, string contentType, string body, CancellationToken ct)
    {
        var content = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(content, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
