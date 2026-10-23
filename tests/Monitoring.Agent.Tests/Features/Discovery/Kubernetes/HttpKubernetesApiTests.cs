using System.Net;
using System.Text;
using System.Text.Json;
using Monitoring.Agent.Features.Discovery.Kubernetes;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Agent.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class HttpKubernetesApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListPodsAsync_gets_the_pods_of_the_namespace()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, ApiJson.List("10"));

        // Act
        await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        server.Requests.Single().ShouldBe("GET api/v1/namespaces/shop/pods");
    }

    [Fact]
    public async Task ListWarningEventsAsync_asks_for_warnings_only_and_reads_what_each_is_about()
    {
        // Arrange
        const string json = """
            {"items":[{"involvedObject":{"kind":"Pod","name":"orders-1"},"reason":"FailedScheduling","message":"0/3 nodes are available","lastTimestamp":"2026-11-02T08:00:00Z"}]}
            """;
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, json);

        // Act
        var events = await server.Api.ListWarningEventsAsync("shop", Ct);

        // Assert
        server.Requests.Single().ShouldBe("GET api/v1/namespaces/shop/events?fieldSelector=type%3DWarning");
        events.Single().InvolvedObject!.Name.ShouldBe("orders-1");
        events.Single().Reason.ShouldBe("FailedScheduling");
        events.Single().LastTimestamp.ShouldBe(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ListWarningEventsAsync_fails_with_an_api_exception_when_the_role_may_not_read_events()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.Forbidden, "{}");

        // Act
        var list = () => server.Api.ListWarningEventsAsync("shop", Ct);

        // Assert
        await list.ShouldThrowAsync<KubernetesApiException>();
    }

    [Fact]
    public async Task ListPodUsageAsync_adds_up_the_containers_of_each_pod()
    {
        // Arrange
        const string json = """
            {"items":[{"metadata":{"name":"orders-1"},"containers":[{"name":"app","usage":{"cpu":"150m","memory":"100Mi"}},{"name":"sidecar","usage":{"cpu":"2m","memory":"20Mi"}}]}]}
            """;
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, json);

        // Act
        var usage = await server.Api.ListPodUsageAsync("shop", Ct);

        // Assert
        server.Requests.Single().ShouldBe("GET apis/metrics.k8s.io/v1beta1/namespaces/shop/pods");
        usage["orders-1"].CpuCores.ShouldBe(0.152, tolerance: 1e-9);
        usage["orders-1"].MemoryBytes.ShouldBe(120 * 1024 * 1024);
    }

    [Fact]
    public async Task ListPodUsageAsync_fails_with_an_api_exception_when_there_is_no_metrics_server()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.NotFound, "{}");

        // Act
        var list = () => server.Api.ListPodUsageAsync("shop", Ct);

        // Assert
        await list.ShouldThrowAsync<KubernetesApiException>();
    }

    [Fact]
    public async Task ListWorkloadsAsync_reads_deployments_and_statefulsets_by_kind_and_name()
    {
        // Arrange
        const string deployments = """{"items":[{"metadata":{"name":"orders","generation":4},"spec":{"replicas":3},"status":{"observedGeneration":4,"readyReplicas":2,"updatedReplicas":1,"replicas":3}}]}""";
        const string statefulsets = """{"items":[{"metadata":{"name":"db"},"spec":{"replicas":2},"status":{"readyReplicas":2,"updatedReplicas":2}}]}""";
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, deployments, statefulsets);

        // Act
        var workloads = await server.Api.ListWorkloadsAsync("shop", Ct);

        // Assert
        server.Requests.ShouldBe(["GET apis/apps/v1/namespaces/shop/deployments", "GET apis/apps/v1/namespaces/shop/statefulsets"]);
        workloads.Keys.Order().ShouldBe(["Deployment/orders", "StatefulSet/db"]);
        workloads["Deployment/orders"].Status!.ReadyReplicas.ShouldBe(2);
    }

    [Fact]
    public async Task ListWorkloadsAsync_fails_with_an_api_exception_when_the_role_may_not_read_them()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.Forbidden, "{}");

        // Act
        var list = () => server.Api.ListWorkloadsAsync("shop", Ct);

        // Assert
        await list.ShouldThrowAsync<KubernetesApiException>();
    }

    [Fact]
    public async Task ListPodsAsync_escapes_the_namespace_in_the_path()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, ApiJson.List("10"));

        // Act
        await server.Api.ListPodsAsync("a/b c", Ct);

        // Assert
        server.Requests.Single().ShouldBe("GET api/v1/namespaces/a%2Fb%20c/pods");
    }

    [Fact]
    public async Task ListPodsAsync_returns_the_pods_and_the_version_to_watch_from()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, ApiJson.List("42", new PodBuilder().Named("a").Build(), new PodBuilder().Named("b").Build()));

        // Act
        var result = await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        result.ResourceVersion.ShouldBe("42");
        result.Pods.Select(p => p.Metadata.Name).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task ListPodsAsync_reads_what_the_api_says_about_each_container()
    {
        // Arrange
        const string json = """
            {"metadata":{"resourceVersion":"7"},"items":[{"metadata":{"name":"orders-1"},
              "spec":{"containers":[{"name":"orders","image":"orders:1"}],"initContainers":[{"name":"proxy","restartPolicy":"Always"}]},
              "status":{"phase":"Running",
                "initContainerStatuses":[{"name":"proxy","ready":true,"state":{"running":{"startedAt":"2026-10-21T10:00:00Z"}}}],
                "containerStatuses":[{"name":"orders","ready":false,"restartCount":2,"image":"orders:1","imageID":"docker://sha256:abc",
                  "state":{"running":{"startedAt":"2026-10-21T10:00:00Z"}},
                  "lastState":{"terminated":{"exitCode":137,"reason":"OOMKilled"}}}]}}]}
            """;
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, json);

        // Act
        var result = await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        var pod = result.Pods.Single();
        var container = pod.Status!.ContainerStatuses!.Single();
        container.ImageID.ShouldBe("docker://sha256:abc");
        container.State!.Running!.StartedAt.ShouldBe("2026-10-21T10:00:00Z");
        container.LastState!.Terminated!.Reason.ShouldBe("OOMKilled");
        container.LastState.Terminated.ExitCode.ShouldBe(137);
        pod.Spec!.InitContainers!.Single().RestartPolicy.ShouldBe("Always");
        pod.Status.InitContainerStatuses!.Single().Name.ShouldBe("proxy");
    }

    [Fact]
    public async Task ListPodsAsync_returns_no_pods_when_the_list_has_no_items()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, """{"metadata":{"resourceVersion":"7"}}""");

        // Act
        var result = await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        result.Pods.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListPodsAsync_fails_when_the_answer_has_no_resource_version()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, """{"items":[]}""");

        // Act
        var list = async () => await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        await list.ShouldThrowAsync<KubernetesApiException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    public async Task ListPodsAsync_reports_the_status_code_of_an_error_answer(HttpStatusCode status, int expected)
    {
        // Arrange
        var server = new FakeApiServer().Answering(status, """{"message":"nope"}""");

        // Act
        var list = async () => await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        var exception = await list.ShouldThrowAsync<KubernetesApiException>();
        exception.StatusCode.ShouldBe(expected);
        exception.Message.ShouldContain("nope");
    }

    [Fact]
    public async Task ListPodsAsync_fails_clearly_on_unreadable_json()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, "not json");

        // Act
        var list = async () => await server.Api.ListPodsAsync("shop", Ct);

        // Assert
        await list.ShouldThrowAsync<KubernetesApiException>();
    }

    [Fact]
    public async Task WatchPodsAsync_asks_for_bookmarks_the_duration_and_the_version()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, "");

        // Act
        await server.Api.WatchPodsAsync("shop", "42", TimeSpan.FromSeconds(59.2), Ct).ToListAsync(Ct);

        // Assert
        server.Requests.Single().ShouldBe(
            "GET api/v1/namespaces/shop/pods?watch=true&allowWatchBookmarks=true&timeoutSeconds=60&resourceVersion=42");
    }

    [Theory]
    [InlineData("ADDED", "Added")]
    [InlineData("MODIFIED", "Modified")]
    [InlineData("DELETED", "Deleted")]
    public async Task WatchPodsAsync_yields_the_pod_and_the_version_of_each_change(string kind, string expected)
    {
        // Arrange
        var pod = new PodBuilder().Named("orders-1").AtVersion("55").Build();
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, ApiJson.Event(kind, pod));

        // Act
        var events = await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        var podEvent = events.Single();
        podEvent.Type.ToString().ShouldBe(expected);
        podEvent.Pod!.Metadata.Name.ShouldBe("orders-1");
        podEvent.ResourceVersion.ShouldBe("55");
    }

    [Fact]
    public async Task WatchPodsAsync_yields_a_bookmark_with_only_the_version()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, """{"type":"BOOKMARK","object":{"metadata":{"resourceVersion":"99"}}}""");

        // Act
        var events = await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        events.Single().ShouldBe(new PodEvent(PodEventType.Bookmark, null, "99"));
    }

    [Fact]
    public async Task WatchPodsAsync_yields_events_in_order_and_skips_blank_lines_and_unknown_types()
    {
        // Arrange
        var body = string.Join("\n",
            ApiJson.Event("ADDED", new PodBuilder().Named("a").Build()),
            "",
            """{"type":"SOMETHING_NEW","object":{}}""",
            ApiJson.Event("DELETED", new PodBuilder().Named("b").Build()));
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, body);

        // Act
        var events = await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        events.Select(e => (e.Type, e.Pod!.Metadata.Name)).ShouldBe([(PodEventType.Added, "a"), (PodEventType.Deleted, "b")]);
    }

    [Fact]
    public async Task WatchPodsAsync_signals_an_expired_version_reported_inside_the_stream()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, """{"type":"ERROR","object":{"code":410,"message":"too old"}}""");

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        await watch.ShouldThrowAsync<KubernetesResourceExpiredException>();
    }

    [Fact]
    public async Task WatchPodsAsync_reports_any_other_error_inside_the_stream()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, """{"type":"ERROR","object":{"code":500,"message":"broken"}}""");

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        var exception = await watch.ShouldThrowAsync<KubernetesApiException>();
        exception.StatusCode.ShouldBe(500);
        exception.Message.ShouldContain("broken");
    }

    [Fact]
    public async Task WatchPodsAsync_signals_an_expired_version_answered_with_http_410()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.Gone, """{"message":"too old"}""");

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        await watch.ShouldThrowAsync<KubernetesResourceExpiredException>();
    }

    [Fact]
    public async Task WatchPodsAsync_reports_an_http_error()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.InternalServerError, "oops");

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        (await watch.ShouldThrowAsync<KubernetesApiException>()).StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task WatchPodsAsync_fails_clearly_on_an_unreadable_line()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, "not json");

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), Ct).ToListAsync(Ct);

        // Assert
        await watch.ShouldThrowAsync<KubernetesApiException>();
    }

    [Fact]
    public async Task WatchPodsAsync_honours_cancellation()
    {
        // Arrange
        var server = new FakeApiServer().Answering(HttpStatusCode.OK, ApiJson.Event("ADDED", new PodBuilder().Build()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var watch = async () => await server.Api.WatchPodsAsync("shop", "1", TimeSpan.FromSeconds(60), cancelled.Token).ToListAsync(CancellationToken.None);

        // Assert
        await watch.ShouldThrowAsync<OperationCanceledException>();
    }

    /// <summary>Serialises API answers the way the Kubernetes API does.</summary>
    private static class ApiJson
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public static string List(string version, params Pod[] pods) =>
            JsonSerializer.Serialize(new PodList(new ListMetadata(version), [.. pods]), Options);

        public static string Event(string kind, Pod pod) =>
            JsonSerializer.Serialize(new { type = kind, @object = pod }, Options);
    }

    /// <summary>An API server stand-in that answers every request the same way and records what was asked.</summary>
    private sealed class FakeApiServer
    {
        private readonly List<string> _requests = [];
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "";
        private List<string> _bodies = [];

        public IReadOnlyList<string> Requests => _requests;

        public HttpKubernetesApi Api => new(new HttpClient(new StubHandler(this)) { BaseAddress = new Uri("https://kubernetes.test/") });

        /// <summary>Answers with these bodies in turn, the last one again and again.</summary>
        public FakeApiServer Answering(HttpStatusCode status, params string[] bodies)
        {
            _status = status;
            _bodies = [.. bodies];
            _body = bodies[0];
            return this;
        }

        private string NextBody()
        {
            // The bodies are given in turn and the last one repeats.
            var body = _bodies.Count > 0 ? _bodies[0] : _body;
            if (_bodies.Count > 1)
            {
                _bodies.RemoveAt(0);
            }

            return body;
        }

        private sealed class StubHandler(FakeApiServer server) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                server._requests.Add($"{request.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}");
                return Task.FromResult(new HttpResponseMessage(server._status)
                {
                    Content = new StringContent(server.NextBody(), Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
