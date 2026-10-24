using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Budgets;

/// <summary>
/// The dashboard pushed to many open pages at once, over real connections and the real clock. Each page makes the controller build and serialize the overview
/// itself, so the cost grows with the number of pages. The limits are many times above what was measured, so a shared runner's noise does not fail the build
/// but a change that makes a page cost an order of magnitude more does. The test prints what it measured.
/// </summary>
// Runs alone, like the scale budgets: beside the other tests on a shared runner it took 16 s instead of 0.3 s and starved their threads.
[Collection("Scale budgets")]
public class FanOutBudgetTests(ITestOutputHelper output)
{
    private const int Pages = 200;
    private const int Scopes = 20;
    private const int InstancesPerScope = 50;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_hundred_open_pages_all_get_the_overview_and_the_next_change_quickly()
    {
        // Arrange
        var registry = new SwappableRegistry(HealthState.Healthy);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IScopeRegistry>(registry);
        builder.Services.AddSingleton<IAgentLiveness>(new FakeAgentLiveness());
        builder.Services.AddSingleton<IHealthRollup, HealthRollup>();
        builder.Services.AddSingleton<IAlertState>(new NoAlerts());
        builder.Services.AddSingleton<IAppliedConfigurations>(new AppliedConfigurations(new EffectiveConfiguration(Microsoft.Extensions.Logging.Abstractions.NullLogger<EffectiveConfiguration>.Instance)));
        builder.Services.AddDashboard(o => o.PushInterval = TimeSpan.FromMilliseconds(250));
        await using var app = builder.Build();
        app.MapDashboard();
        await app.StartAsync(Ct);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = int.MaxValue }) { Timeout = Timeout.InfiniteTimeSpan };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var first = new TaskCompletionSource[Pages];
        var second = new TaskCompletionSource[Pages];
        var readers = new Task[Pages];
        for (var page = 0; page < Pages; page++)
        {
            first[page] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            second[page] = new(TaskCreationOptions.RunContinuationsAsynchronously);
            readers[page] = ReadOverviewsAsync(http, $"{address}/api/events", first[page], second[page], stop.Token);
        }

        // Act: all pages open, then every instance changes at once
        var opening = Stopwatch.StartNew();
        await Task.WhenAll(first.Select(p => p.Task)).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var firstEvent = opening.Elapsed;

        var changing = Stopwatch.StartNew();
        registry.Replace(HealthState.Unhealthy);
        await Task.WhenAll(second.Select(p => p.Task)).WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var change = changing.Elapsed;

        await stop.CancelAsync();
        await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        // Assert
        output.WriteLine($"{Pages} pages x {Scopes * InstancesPerScope} instances: first overview on every page after {firstEvent.TotalMilliseconds:F0} ms, a change on every page after {change.TotalMilliseconds:F0} ms (push interval 250 ms)");
        firstEvent.ShouldBeLessThan(TimeSpan.FromSeconds(10), "every page is served its first overview at once");
        change.ShouldBeLessThan(TimeSpan.FromSeconds(10), "a change reaches every page within a few push intervals");
    }

    private static async Task ReadOverviewsAsync(HttpClient http, string url, TaskCompletionSource first, TaskCompletionSource second, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            var seen = 0;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line == "event: overview" && ++seen == 1)
                {
                    first.TrySetResult();
                }
                else if (line == "event: overview" && seen == 2)
                {
                    second.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The test is over.
        }
        catch (Exception e)
        {
            first.TrySetException(e);
            second.TrySetException(e);
        }
    }

    /// <summary>A registry whose whole content is replaced in one step, so a stream that is reading never sees a half-made change.</summary>
    private sealed class SwappableRegistry : IScopeRegistry
    {
        private volatile Dictionary<string, List<InstanceInfo>> _scopes;

        public SwappableRegistry(HealthState health) => _scopes = Build(health);

        public void Replace(HealthState health) => _scopes = Build(health);

        private static Dictionary<string, List<InstanceInfo>> Build(HealthState health) =>
            Enumerable.Range(0, Scopes).ToDictionary(
                scope => $"scope-{scope:D2}",
                scope => Enumerable.Range(0, InstancesPerScope).Select(i => new InstanceInfo(
                    $"orders-{scope:D2}-{i:D3}", $"service-{i % 10}", InstanceLifecycle.Running, health, new Dictionary<string, string>())).ToList());

        public IReadOnlyList<InstanceInfo> GetInstances(string scopeName) => _scopes.TryGetValue(scopeName, out var list) ? list : [];

        public IReadOnlyDictionary<string, string> GetScopeLabels(string scopeName) => new Dictionary<string, string>();

        public IReadOnlyList<string> GetScopeNames() => [.. _scopes.Keys.Order(StringComparer.Ordinal)];

        public ApplyResult ApplySnapshot(Snapshot snapshot) => throw new NotSupportedException();

        public ApplyResult ApplyDelta(string scopeName, Delta delta) => throw new NotSupportedException();

        public void PurgeExpired()
        {
        }
    }

    private sealed class NoAlerts : IAlertState
    {
        public IReadOnlyList<Alert> GetActiveAlerts() => [];
    }
}
