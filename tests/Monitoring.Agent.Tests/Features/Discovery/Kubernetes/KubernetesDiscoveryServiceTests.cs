using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.Discovery.Kubernetes;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Agent.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class KubernetesDiscoveryServiceTests
{
    private static Pod Running(string name, bool ready = true) =>
        ready
            ? new PodBuilder().Named(name).InPhase("Running").PodReady().WithContainer("app", ready: true).Build()
            : new PodBuilder().Named(name).InPhase("Running").WithContainer("app", ready: false).Build();

    [Fact]
    public async Task Service_puts_the_listed_pods_into_the_scope_state()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a"), Running("b"));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 2);

        // Assert
        fixture.InstanceIds().ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Service_publishes_the_listed_pods_as_one_delta()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a"), Running("b"));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Deltas.Count == 1);

        // Assert
        fixture.Deltas.Single().Upserted.Select(i => i.Id).ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Service_shows_the_latest_warning_of_the_cluster_on_a_pod_that_is_not_healthy()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a", ready: false))
            .WithWarning("a", "FailedScheduling", "older", minutesAgo: 5)
            .WithWarning("a", "FailedScheduling", "0/3 nodes are available");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        var check = fixture.Instance("a")!.Checks!.Single(c => c.Name == "kubernetes event");
        check.Description.ShouldBe("FailedScheduling: 0/3 nodes are available");
    }

    [Fact]
    public async Task Service_shows_on_each_pod_of_a_deployment_that_it_is_rolling_out()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10",
            new PodBuilder().Named("orders-7d9f-a").InPhase("Running").PodReady().WithContainer("app", ready: true).OwnedBy("ReplicaSet", "orders-7d9f").Build(),
            new PodBuilder().Named("billing-1").InPhase("Running").PodReady().WithContainer("app", ready: true).OwnedBy("ReplicaSet", "billing-55c8").Build());
        fixture.Api.Workloads = new Dictionary<string, KubernetesWorkload>
        {
            ["Deployment/orders"] = new(new WorkloadMetadata("orders", 4), new WorkloadSpec(3), new WorkloadStatus(4, ReadyReplicas: 2, UpdatedReplicas: 1)),
            ["Deployment/billing"] = new(new WorkloadMetadata("billing", 2), new WorkloadSpec(1), new WorkloadStatus(2, ReadyReplicas: 1, UpdatedReplicas: 1)),
        };

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 2);

        // Assert
        fixture.Instance("orders-7d9f-a")!.Checks!.Single(c => c.Name == "rollout").Description.ShouldBe("Rolling out: 1 of 3 replicas updated, 2 ready.");
        fixture.Instance("billing-1")!.Checks.ShouldBeNull();
    }

    [Fact]
    public async Task Service_still_follows_the_pods_when_the_workloads_cannot_be_read()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a"));
        fixture.Api.Workloads = new KubernetesApiException("forbidden");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Checks.ShouldBeNull();
    }

    [Fact]
    public async Task Service_shows_what_a_pod_uses_beside_what_it_asked_for_when_asked_to_read_the_metrics_api()
    {
        // Arrange
        var fixture = new DiscoveryFixture { ReadPodMetrics = true }.ListingPods("10",
            new PodBuilder().Named("a").InPhase("Running").PodReady().WithResources("app", new() { ["cpu"] = "250m", ["memory"] = "128Mi" }, new() { ["memory"] = "256Mi" }).Build());
        fixture.Api.Usage = new Dictionary<string, PodUsage> { ["a"] = new(0.1, 100 * 1024 * 1024) };

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Metrics!.ToDictionary(m => m.Name, m => m.Value).ShouldBe(new Dictionary<string, double>
        {
            ["k8s.pod.cpu_usage_cores"] = 0.1,
            ["k8s.pod.memory_usage_bytes"] = 100 * 1024 * 1024,
            ["k8s.pod.cpu_request_cores"] = 0.25,
            ["k8s.pod.memory_request_bytes"] = 128 * 1024 * 1024,
            ["k8s.pod.memory_limit_bytes"] = 256 * 1024 * 1024,
        });
    }

    [Fact]
    public async Task Service_names_the_kubernetes_services_that_select_a_pod_on_it()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", new PodBuilder().Named("orders-1").InPhase("Running").PodReady().WithLabel("app", "orders").Build(), Running("other"));
        fixture.Api.Services = new List<KubernetesService>
        {
            new(new PodMetadata("orders-svc"), new ServiceSpec(new() { ["app"] = "orders" }, "10.96.0.7")),
            new(new PodMetadata("orders-headless"), new ServiceSpec(new() { ["app"] = "orders" }, "None")),
            new(new PodMetadata("elsewhere"), new ServiceSpec(new() { ["app"] = "billing" }, "10.96.0.9")),
            new(new PodMetadata("external-name"), new ServiceSpec(null, null)),
        };

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 2);

        // Assert
        fixture.Instance("orders-1")!.Labels["k8s.services"].ShouldBe("orders-headless,orders-svc");
        fixture.Instance("orders-1")!.Labels["k8s.service-ips"].ShouldBe("10.96.0.7");
        fixture.Instance("other")!.Labels.ShouldNotContainKey("k8s.services");
    }

    [Fact]
    public async Task Service_still_follows_the_pods_when_the_services_cannot_be_read()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a"));
        fixture.Api.Services = new KubernetesApiException("forbidden");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Labels.ShouldNotContainKey("k8s.services");
    }

    [Fact]
    public async Task Service_does_not_ask_for_the_use_of_pods_unless_told_to()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a"));
        fixture.Api.Usage = new Dictionary<string, PodUsage> { ["a"] = new(1, 1) };

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Api.UsageCalls.ShouldBe(0);
        fixture.Instance("a")!.Metrics.ShouldBeNull();
    }

    [Fact]
    public async Task Service_still_follows_the_pods_when_there_is_no_metrics_server()
    {
        // Arrange
        var fixture = new DiscoveryFixture { ReadPodMetrics = true }.ListingPods("10", Running("a"));
        fixture.Api.Usage = new KubernetesApiException("the server could not find the requested resource");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Metrics.ShouldBeNull();
    }

    [Fact]
    public async Task Service_does_not_show_a_warning_on_a_pod_that_is_healthy()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a")).WithWarning("a", "BackOff", "earlier");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Checks.ShouldBeNull();
    }

    [Fact]
    public async Task Service_still_follows_the_pods_when_the_events_cannot_be_read()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("10", Running("a", ready: false));
        fixture.Api.Events = new KubernetesApiException("forbidden");

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.Instance("a")!.Checks.ShouldBeNull();
    }

    [Fact]
    public async Task Service_watches_from_the_version_of_the_list()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("77", Running("a"));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Api.WatchVersions.Count == 1);

        // Assert
        fixture.Api.WatchVersions.ShouldBe(["77"]);
    }

    [Fact]
    public async Task Service_adds_a_pod_the_watch_reports()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1").WatchYielding(PodEvents.Added(Running("new")));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.InstanceIds().ShouldBe(["new"]);
    }

    [Fact]
    public async Task Service_updates_a_pod_the_watch_reports_as_changed()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1", Running("a")).WatchYielding(PodEvents.Modified(Running("a", ready: false)));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Instance("a")?.Health == HealthState.Degraded);

        // Assert
        fixture.Instance("a")!.Health.ShouldBe(HealthState.Degraded);
    }

    [Fact]
    public async Task Service_removes_a_pod_the_watch_reports_as_deleted()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1", Running("a"), Running("b")).WatchYielding(PodEvents.Deleted(Running("a")));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.InstanceIds().ShouldBe(["b"]);
    }

    [Fact]
    public async Task Service_removes_a_pod_that_completed_successfully()
    {
        // Arrange
        var completed = new PodBuilder().Named("job-1").InPhase("Succeeded").Build();
        var fixture = new DiscoveryFixture().ListingPods("1", Running("job-1")).WatchYielding(PodEvents.Modified(completed));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.InstanceIds().Count == 0 && fixture.Deltas.Count == 2);

        // Assert
        fixture.InstanceIds().ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_publishes_nothing_for_a_change_that_leaves_the_instance_the_same()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1", Running("a")).WatchYielding(PodEvents.Modified(Running("a")), PodEvents.Bookmark("2"));

        // Act
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Api.WatchesStarted == 1);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert: only the initial list produced a delta
        fixture.Deltas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Service_lists_again_after_the_watch_ends_and_corrects_what_it_missed()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListingPods("1", Running("a"), Running("gone"))
            .WatchYielding()
            .ListingPods("2", Running("a"), Running("fresh"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.Api.ListCalls >= 2 && fixture.InstanceIds().SequenceEqual(["a", "fresh"]));

        // Assert
        fixture.InstanceIds().ShouldBe(["a", "fresh"]);
    }

    [Fact]
    public async Task Service_publishes_nothing_when_a_new_list_matches_what_it_already_has()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListingPods("1", Running("a"))
            .WatchYielding()
            .ListingPods("2", Running("a"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.Api.ListCalls >= 2);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        fixture.Deltas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Service_waits_before_listing_again_when_the_watch_ended_at_once()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1", Running("a")).WatchYielding();
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Api.WatchesStarted == 1);

        // Act
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert: no time passed, so no second list
        fixture.Api.ListCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Service_lists_again_when_the_watch_version_expired()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListingPods("1", Running("a"))
            .WatchFailing(new KubernetesResourceExpiredException("too old"))
            .ListingPods("2", Running("a"), Running("b"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.InstanceIds().Count == 2);

        // Assert
        fixture.InstanceIds().ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Service_retries_after_a_failed_list()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListFailing(new KubernetesApiException("forbidden", 403))
            .ListingPods("1", Running("a"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.InstanceIds().ShouldBe(["a"]);
    }

    [Fact]
    public async Task Service_retries_after_the_connection_failed()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListFailing(new HttpRequestException("connection refused"))
            .ListingPods("1", Running("a"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.InstanceIds().Count == 1);

        // Assert
        fixture.InstanceIds().ShouldBe(["a"]);
    }

    [Fact]
    public async Task Service_lists_again_after_the_watch_failed()
    {
        // Arrange
        var fixture = new DiscoveryFixture()
            .ListingPods("1", Running("a"))
            .WatchFailing(new IOException("connection reset"))
            .ListingPods("2", Running("a"), Running("b"));

        // Act
        await fixture.StartAsync();
        await fixture.AdvanceUntilAsync(() => fixture.InstanceIds().Count == 2);

        // Assert
        fixture.InstanceIds().ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task Service_stops_without_throwing_when_the_host_stops()
    {
        // Arrange
        var fixture = new DiscoveryFixture().ListingPods("1", Running("a"));
        await fixture.StartAsync();
        await fixture.WaitUntilAsync(() => fixture.Api.WatchesStarted == 1);

        // Act
        var stop = async () => await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Builds the service over a scripted API, a real scope state and a fake clock; retries wait exactly one second.</summary>
    private sealed class DiscoveryFixture
    {
        private readonly KubernetesDiscoveryOptions _options = new()
        {
            ResyncInterval = TimeSpan.FromSeconds(60),
            RetryMinDelay = TimeSpan.FromSeconds(1),
            RetryMaxDelay = TimeSpan.FromSeconds(1),
        };

        private readonly ScopeStateStore _store = new(new ScopeStateOptions { ScopeName = "shop" });
        private readonly StateSubscription _subscription;
        private readonly List<Delta> _seen = [];

        public DiscoveryFixture()
        {
            _subscription = _store.Subscribe();
        }

        public bool ReadPodMetrics { set => _options.PodMetrics = value; }

        public FakeTimeProvider Clock { get; } = new();

        public ScriptedApi Api { get; } = new();

        public KubernetesDiscoveryService Service { get; private set; } = null!;

        /// <summary>Every delta the scope state has published so far.</summary>
        public IReadOnlyList<Delta> Deltas
        {
            get
            {
                var seen = new List<Delta>(_seen);
                while (_subscription.Deltas.TryRead(out var delta))
                {
                    _seen.Add(delta);
                    seen.Add(delta);
                }

                return seen;
            }
        }

        public DiscoveryFixture ListingPods(string version, params Pod[] pods)
        {
            Api.Lists.Enqueue(new PodListResult(version, pods));
            return this;
        }

        public DiscoveryFixture WithWarning(string pod, string reason, string message, int minutesAgo = 0)
        {
            var events = ((IReadOnlyList<KubernetesEvent>)Api.Events).ToList();
            events.Add(new KubernetesEvent(new ObjectReference("Pod", pod), reason, message, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero).AddMinutes(-minutesAgo)));
            Api.Events = events;
            return this;
        }

        public DiscoveryFixture ListFailing(Exception exception)
        {
            Api.Lists.Enqueue(exception);
            return this;
        }

        /// <summary>The next watch yields these events and then ends.</summary>
        public DiscoveryFixture WatchYielding(params PodEvent[] events)
        {
            Api.Watches.Enqueue(ct => Yield(events));
            return this;
        }

        public DiscoveryFixture WatchFailing(Exception exception)
        {
            Api.Watches.Enqueue(ct => Fail(exception));
            return this;
        }

        public async Task StartAsync()
        {
            Service = new KubernetesDiscoveryService(
                Api, _store, new DiscoveryTarget("shop"), _options, Clock, NullLogger<KubernetesDiscoveryService>.Instance);
            await Service.StartAsync(TestContext.Current.CancellationToken);
        }

        public IReadOnlyList<string> InstanceIds() => [.. _store.CreateSnapshot().Instances.Select(i => i.Id)];

        public InstanceInfo? Instance(string id) =>
            _store.CreateSnapshot().Instances.FirstOrDefault(i => i.Id == id);

        /// <summary>Waits until the condition holds; fails instead of hanging.</summary>
        public async Task WaitUntilAsync(Func<bool> condition)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                await Task.Delay(5, guard.Token);
            }
        }

        /// <summary>Moves the clock forward in one-second steps until the condition holds, for waits the service sits in.</summary>
        public async Task AdvanceUntilAsync(Func<bool> condition)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(10, guard.Token);
            }
        }

        private static async IAsyncEnumerable<PodEvent> Yield(PodEvent[] events)
        {
            foreach (var podEvent in events)
            {
                yield return podEvent;
            }

            await Task.CompletedTask;
        }

        private static async IAsyncEnumerable<PodEvent> Fail(Exception exception)
        {
            await Task.CompletedTask;
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    /// <summary>API that plays back scripted answers; once the script runs out, lists repeat the last answer and watches stay open.</summary>
    private sealed class ScriptedApi : IKubernetesApi
    {
        private PodListResult _lastList = new("0", []);
        private int _listCalls;
        private int _watchesStarted;

        public Queue<object> Lists { get; } = new();

        /// <summary>The warning events the API answers with, or the exception it fails with.</summary>
        public object Events { get; set; } = (IReadOnlyList<KubernetesEvent>)[];

        public Queue<Func<CancellationToken, IAsyncEnumerable<PodEvent>>> Watches { get; } = new();

        public List<string> WatchVersions { get; } = [];

        public int ListCalls => Volatile.Read(ref _listCalls);

        public int WatchesStarted => Volatile.Read(ref _watchesStarted);

        public Task<PodListResult> ListPodsAsync(string namespaceName, CancellationToken ct)
        {
            Interlocked.Increment(ref _listCalls);
            if (Lists.TryDequeue(out var next))
            {
                if (next is Exception exception)
                {
                    throw exception;
                }

                _lastList = (PodListResult)next;
            }

            return Task.FromResult(_lastList);
        }

        /// <summary>The workloads the API answers with, or the exception it fails with.</summary>
        public object Workloads { get; set; } = (IReadOnlyDictionary<string, KubernetesWorkload>)new Dictionary<string, KubernetesWorkload>();

        public Task<IReadOnlyDictionary<string, KubernetesWorkload>> ListWorkloadsAsync(string namespaceName, CancellationToken ct) =>
            Workloads is Exception exception ? throw exception : Task.FromResult((IReadOnlyDictionary<string, KubernetesWorkload>)Workloads);

        /// <summary>The Services the API answers with, or the exception it fails with.</summary>
        public object Services { get; set; } = (IReadOnlyList<KubernetesService>)[];

        public Task<IReadOnlyList<KubernetesService>> ListServicesAsync(string namespaceName, CancellationToken ct) =>
            Services is Exception exception ? throw exception : Task.FromResult((IReadOnlyList<KubernetesService>)Services);

        /// <summary>What the metrics API answers with, or the exception it fails with.</summary>
        public object Usage { get; set; } = (IReadOnlyDictionary<string, PodUsage>)new Dictionary<string, PodUsage>();

        public int UsageCalls { get; private set; }

        public Task<IReadOnlyDictionary<string, PodUsage>> ListPodUsageAsync(string namespaceName, CancellationToken ct)
        {
            UsageCalls++;
            return Usage is Exception exception ? throw exception : Task.FromResult((IReadOnlyDictionary<string, PodUsage>)Usage);
        }

        public Task<IReadOnlyList<KubernetesEvent>> ListWarningEventsAsync(string namespaceName, CancellationToken ct) =>
            Events is Exception exception ? throw exception : Task.FromResult((IReadOnlyList<KubernetesEvent>)Events);

        public IAsyncEnumerable<PodEvent> WatchPodsAsync(string namespaceName, string resourceVersion, TimeSpan duration, CancellationToken ct)
        {
            lock (WatchVersions)
            {
                WatchVersions.Add(resourceVersion);
            }

            Interlocked.Increment(ref _watchesStarted);
            return Watches.TryDequeue(out var script) ? script(ct) : Idle(ct);
        }

        private static async IAsyncEnumerable<PodEvent> Idle([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
    }

    /// <summary>Creates watch events for pods.</summary>
    private static class PodEvents
    {
        public static PodEvent Added(Pod pod) => new(PodEventType.Added, pod, pod.Metadata.ResourceVersion);

        public static PodEvent Modified(Pod pod) => new(PodEventType.Modified, pod, pod.Metadata.ResourceVersion);

        public static PodEvent Deleted(Pod pod) => new(PodEventType.Deleted, pod, pod.Metadata.ResourceVersion);

        public static PodEvent Bookmark(string version) => new(PodEventType.Bookmark, null, version);
    }
}
