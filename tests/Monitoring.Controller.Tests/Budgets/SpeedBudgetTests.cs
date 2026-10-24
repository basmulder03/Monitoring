using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Budgets;

/// <summary>
/// How fast a controller works at the size of <see cref="ScaleBudgetTests"/> (5,000 instances, 50 series each). The limits are about ten times above what was measured on a laptop (2 ms to apply, 90 ms and 230 ms to evaluate), so a shared runner's
/// noise does not fail the build but a change that makes the work an order of magnitude slower does. Each test prints what it measured.
/// </summary>
public class SpeedBudgetTests(ITestOutputHelper output)
{
    private static InstanceInfo Instance(int number, double value) => new(
        $"orders-{number:D5}",
        $"service-{number % 100}",
        InstanceLifecycle.Running,
        HealthState.Healthy,
        new Dictionary<string, string> { ["app"] = $"service-{number % 100}" },
        Version: "1.2.3",
        Metrics: [.. Enumerable.Range(0, 50).Select(i => new MetricSeries($"http.server.request.duration.series{i}", new Dictionary<string, string> { ["route"] = "/orders" }, value + i))]);

    [Fact]
    public void Applying_a_round_of_deltas_from_fifty_agents_with_a_hundred_instances_each_is_fast()
    {
        // Arrange
        var registry = new ScopeRegistry(new RegistryOptions(), new FakeTimeProvider());
        for (var scope = 0; scope < 50; scope++)
        {
            registry.ApplySnapshot(new Snapshot(1, $"scope-{scope}", [.. Enumerable.Range(scope * 100, 100).Select(n => Instance(n, 0))]));
        }

        var deltas = Enumerable.Range(0, 50).Select(scope => new Delta(2, [.. Enumerable.Range(scope * 100, 100).Select(n => Instance(n, 1))], [])).ToList();

        // Act
        var clock = Stopwatch.StartNew();
        for (var scope = 0; scope < 50; scope++)
        {
            registry.ApplyDelta($"scope-{scope}", deltas[scope]).Outcome.ShouldBe(ApplyOutcome.Applied);
        }

        clock.Stop();

        // Assert
        output.WriteLine($"5000 instances x 50 series updated: {clock.Elapsed.TotalMilliseconds:F0} ms");
        PerfResults.Record("controller.apply.5000x50", clock.Elapsed.TotalMilliseconds, "ms");
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(250), "a round of reports from every agent must be applied well within the time agents wait between reports");
    }

    [Fact]
    public void Evaluating_twenty_rules_that_each_hold_for_five_thousand_subjects_is_fast()
    {
        // Arrange
        var rules = Enumerable.Range(0, 20).Select(r => new BusyRule($"rule-{r}", [.. Enumerable.Range(0, 5000).Select(s => $"scope-{s % 50}/service-{s % 100}/instance-{s}")])).ToList();
        var clock = new FakeTimeProvider();
        var evaluator = new AlertEvaluator(rules, new AlertingOptions { StartupGrace = TimeSpan.Zero }, clock);

        // Act: the first round creates the alerts, the second only keeps them, which is what runs every few seconds
        var first = Stopwatch.StartNew();
        evaluator.Evaluate();
        first.Stop();
        clock.Advance(TimeSpan.FromSeconds(10));
        var again = Stopwatch.StartNew();
        evaluator.Evaluate();
        var alerts = evaluator.GetActiveAlerts();
        again.Stop();

        // Assert
        output.WriteLine($"100000 subjects: first round {first.Elapsed.TotalMilliseconds:F0} ms, later round and listing {again.Elapsed.TotalMilliseconds:F0} ms");
        PerfResults.Record("controller.alerts.first", first.Elapsed.TotalMilliseconds, "ms");
        PerfResults.Record("controller.alerts.later", again.Elapsed.TotalMilliseconds, "ms");
        alerts.Count.ShouldBe(100_000);
        first.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        again.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    private sealed class BusyRule(string name, string[] subjects) : IAlertRule
    {
        public string Name { get; } = name;

        public AlertSeverity Severity => AlertSeverity.Warning;

        public TimeSpan Wait => TimeSpan.Zero;

        public IEnumerable<string> FindFailingSubjects() => subjects;
    }
}
