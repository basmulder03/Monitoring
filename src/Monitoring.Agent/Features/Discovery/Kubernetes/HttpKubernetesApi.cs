using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>
/// Talks to the Kubernetes API with plain HTTP and source-generated JSON. The official client is reflection-heavy and
/// does not work in a Native AOT executable.
/// </summary>
/// <param name="http">Client whose base address is the API server and which adds the credentials.</param>
internal sealed class HttpKubernetesApi(HttpClient http) : IKubernetesApi, IDisposable
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Extra time given to a watch beyond the duration the server was asked for, before the client gives up on a silent connection.</summary>
    private static readonly TimeSpan WatchGrace = TimeSpan.FromSeconds(30);

    /// <summary>Releases the HTTP client.</summary>
    public void Dispose() => http.Dispose();

    /// <inheritdoc />
    public async Task<PodListResult> ListPodsAsync(string namespaceName, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ListTimeout);

        using var response = await http.GetAsync(PodsPath(namespaceName), timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var list = await ReadAsync(() => JsonSerializer.DeserializeAsync(body, KubernetesJsonContext.Default.PodList, timeout.Token).AsTask())
            .ConfigureAwait(false);

        var version = list?.Metadata?.ResourceVersion
            ?? throw new KubernetesApiException("The pod list has no resource version.");
        return new PodListResult(version, list.Items ?? []);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, KubernetesWorkload>> ListWorkloadsAsync(string namespaceName, CancellationToken ct)
    {
        var workloads = new Dictionary<string, KubernetesWorkload>(StringComparer.Ordinal);
        foreach (var (resource, kind) in new[] { ("deployments", "Deployment"), ("statefulsets", "StatefulSet") })
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ListTimeout);

            using var response = await http.GetAsync($"apis/apps/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/{resource}", timeout.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var list = await ReadAsync(() => JsonSerializer.DeserializeAsync(body, KubernetesJsonContext.Default.WorkloadList, timeout.Token).AsTask())
                .ConfigureAwait(false);
            foreach (var workload in list?.Items ?? [])
            {
                if (workload.Metadata?.Name is { } name)
                {
                    workloads[$"{kind}/{name}"] = workload;
                }
            }
        }

        return workloads;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KubernetesEvent>> ListWarningEventsAsync(string namespaceName, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ListTimeout);

        var path = $"api/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/events?fieldSelector=type%3DWarning";
        using var response = await http.GetAsync(path, timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var list = await ReadAsync(() => JsonSerializer.DeserializeAsync(body, KubernetesJsonContext.Default.EventList, timeout.Token).AsTask())
            .ConfigureAwait(false);
        return list?.Items ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KubernetesService>> ListServicesAsync(string namespaceName, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ListTimeout);

        using var response = await http.GetAsync($"api/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/services", timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var list = await ReadAsync(() => JsonSerializer.DeserializeAsync(body, KubernetesJsonContext.Default.ServiceList, timeout.Token).AsTask())
            .ConfigureAwait(false);
        return list?.Items ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, PodUsage>> ListPodUsageAsync(string namespaceName, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ListTimeout);

        using var response = await http.GetAsync($"apis/metrics.k8s.io/v1beta1/namespaces/{Uri.EscapeDataString(namespaceName)}/pods", timeout.Token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, timeout.Token).ConfigureAwait(false);

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var list = await ReadAsync(() => JsonSerializer.DeserializeAsync(body, KubernetesJsonContext.Default.PodMetricsList, timeout.Token).AsTask())
            .ConfigureAwait(false);
        var usage = new Dictionary<string, PodUsage>(StringComparer.Ordinal);
        foreach (var item in list?.Items ?? [])
        {
            if (item.Metadata?.Name is { } name)
            {
                var containers = item.Containers ?? [];
                usage[name] = new PodUsage(
                    containers.Sum(c => Quantity.Parse(c.Usage?.GetValueOrDefault("cpu")) ?? 0),
                    containers.Sum(c => Quantity.Parse(c.Usage?.GetValueOrDefault("memory")) ?? 0));
            }
        }

        return usage;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PodEvent> WatchPodsAsync(
        string namespaceName, string resourceVersion, TimeSpan duration, [EnumeratorCancellation] CancellationToken ct)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));
        var path = $"{PodsPath(namespaceName)}?watch=true&allowWatchBookmarks=true&timeoutSeconds={seconds}"
            + $"&resourceVersion={Uri.EscapeDataString(resourceVersion)}";

        // Ends the watch if the connection goes silent without being closed, which the server's own timeout cannot prevent.
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        guard.CancelAfter(duration + WatchGrace);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, guard, ct).ConfigureAwait(false);
        if (response is null)
        {
            yield break;
        }

        await EnsureSuccessAsync(response, guard.Token).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(guard.Token).ConfigureAwait(false);
        using var reader = new StreamReader(body);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(guard.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (guard.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ParseEvent(line) is { } podEvent)
            {
                yield return podEvent;
            }
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, CancellationTokenSource guard, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, guard.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (guard.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string PodsPath(string namespaceName) => $"api/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/pods";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = $"The Kubernetes API answered {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(text)}";
        throw response.StatusCode == HttpStatusCode.Gone
            ? new KubernetesResourceExpiredException(message)
            : new KubernetesApiException(message, (int)response.StatusCode);
    }

    private static PodEvent? ParseEvent(string line)
    {
        var watchEvent = ReadSync(() => JsonSerializer.Deserialize(line, KubernetesJsonContext.Default.WatchEvent))
            ?? throw new KubernetesApiException("The watch sent an empty event.");

        switch (watchEvent.Type)
        {
            case "ERROR":
                var status = ReadSync(() => watchEvent.Object.Deserialize(KubernetesJsonContext.Default.Status));
                var message = $"The watch failed: {status?.Message ?? status?.Reason ?? "unknown error"}";
                throw status?.Code == 410
                    ? new KubernetesResourceExpiredException(message)
                    : new KubernetesApiException(message, status?.Code ?? 0);

            case "BOOKMARK":
                return new PodEvent(PodEventType.Bookmark, null, ReadSync(() => watchEvent.Object.Deserialize(KubernetesJsonContext.Default.Pod))?.Metadata.ResourceVersion);

            case "ADDED" or "MODIFIED" or "DELETED":
                var pod = ReadSync(() => watchEvent.Object.Deserialize(KubernetesJsonContext.Default.Pod))
                    ?? throw new KubernetesApiException("The watch sent an event without a pod.");
                var type = watchEvent.Type switch
                {
                    "ADDED" => PodEventType.Added,
                    "MODIFIED" => PodEventType.Modified,
                    _ => PodEventType.Deleted,
                };
                return new PodEvent(type, pod, pod.Metadata.ResourceVersion);

            default:
                // An event type from a newer API server: not needed, so it is skipped.
                return null;
        }
    }

    private static T ReadSync<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (JsonException ex)
        {
            throw new KubernetesApiException($"The Kubernetes API sent something unreadable: {ex.Message}");
        }
    }

    private static async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new KubernetesApiException($"The Kubernetes API sent something unreadable: {ex.Message}");
        }
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";
}
