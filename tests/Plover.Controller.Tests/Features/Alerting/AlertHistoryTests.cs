using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.StateStore;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class AlertHistoryTests
{
    private static Alert Firing(string subject, AlertSeverity severity = AlertSeverity.Warning, DateTimeOffset? since = null) =>
        new($"r:{subject}", "r", subject, severity, since ?? DateTimeOffset.UnixEpoch);

    private static IReadOnlyDictionary<string, Alert> Set(params Alert[] alerts) => alerts.ToDictionary(a => a.Key);

    [Fact]
    public void An_alert_is_opened_when_it_starts_to_fire_and_closed_when_it_stops()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var history = new AlertHistory(clock);

        // Act
        history.Update(Set(Firing("shop/a")), clock.GetUtcNow());
        var open = history.Recent(10).Single();
        clock.Advance(TimeSpan.FromMinutes(14));
        history.Update(Set(), clock.GetUtcNow());
        var closed = history.Recent(10).Single();

        // Assert
        open.Ended.ShouldBeNull();
        closed.Ended.ShouldBe(open.Started + TimeSpan.FromMinutes(14));
        closed.EndedBy.ShouldBe("cleared");
    }

    [Fact]
    public void An_alert_that_keeps_firing_is_one_occurrence_and_one_that_comes_back_is_another()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var history = new AlertHistory(clock);

        // Act
        history.Update(Set(Firing("shop/a")), clock.GetUtcNow());
        history.Update(Set(Firing("shop/a")), clock.GetUtcNow());
        history.Update(Set(), clock.GetUtcNow());
        history.Update(Set(Firing("shop/a")), clock.GetUtcNow());

        // Assert
        history.Recent(10).Count.ShouldBe(2);
        history.Recent(10)[0].Ended.ShouldBeNull();
    }

    [Fact]
    public void The_latest_occurrences_come_first_and_only_so_many_are_kept()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var history = new AlertHistory(clock);
        for (var i = 0; i < AlertHistory.Max + 20; i++)
        {
            history.Update(Set(Firing($"s{i}")), clock.GetUtcNow());
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // Act
        var recent = history.Recent(1000);

        // Assert
        recent.Count.ShouldBe(AlertHistory.Max);
        recent[0].Subject.ShouldBe($"s{AlertHistory.Max + 19}");
        history.Recent(3).Count.ShouldBe(3);
    }

    [Fact]
    public void What_was_firing_at_a_restart_is_closed_because_nobody_saw_it_end()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        IStateStore store = new MemoryStateStore();
        var before = new AlertHistory(clock, store);
        before.Update(Set(Firing("shop/a"), Firing("shop/b")), clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(1));
        before.Update(Set(Firing("shop/b")), clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var after = new AlertHistory(clock, store);

        // Assert
        var occurrences = after.Recent(10).ToDictionary(o => o.Subject);
        occurrences["shop/a"].EndedBy.ShouldBe("cleared");
        occurrences["shop/b"].EndedBy.ShouldBe("restart");
        occurrences["shop/b"].Ended.ShouldBe(clock.GetUtcNow());
    }

    [Fact]
    public void The_evaluator_tells_the_history_when_the_wait_of_a_rule_has_passed_even_for_a_silenced_alert()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);
        var history = new AlertHistory(clock);
        var rule = new Rule { Subjects = ["shop/a"] };
        var evaluator = new AlertEvaluator([rule], new AlertingOptions { StartupGrace = TimeSpan.Zero }, clock, actions, history);

        // Act
        evaluator.Evaluate();
        var tooEarly = history.Recent(10).Count;
        clock.Advance(TimeSpan.FromSeconds(30));
        actions.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), null, "ann");
        evaluator.Evaluate();
        var whileSilenced = history.Recent(10).Count;
        rule.Subjects = [];
        evaluator.Evaluate();

        // Assert
        tooEarly.ShouldBe(0);
        whileSilenced.ShouldBe(1);
        history.Recent(10).Single().EndedBy.ShouldBe("cleared");
        evaluator.GetActiveAlerts().ShouldBeEmpty();
    }

    private sealed class Rule : IAlertRule
    {
        public string Name => "r";

        public AlertSeverity Severity => AlertSeverity.Warning;

        public TimeSpan Wait => TimeSpan.FromSeconds(30);

        public string[] Subjects { get; set; } = [];

        public IEnumerable<string> FindFailingSubjects() => Subjects;
    }
}
