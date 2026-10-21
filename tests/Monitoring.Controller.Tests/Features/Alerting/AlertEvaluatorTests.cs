using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class AlertEvaluatorTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(30, 29, false)]
    [InlineData(30, 30, true)]
    [InlineData(30, 90, true)]
    public void GetActiveAlerts_fires_only_after_the_wait_has_passed(int waitSeconds, int elapsedSeconds, bool fires)
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", waitSeconds).Failing("r", "a");
        var evaluator = fixture.Build();
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        // Act
        var alerts = evaluator.GetActiveAlerts();

        // Assert
        alerts.Count.ShouldBe(fires ? 1 : 0);
    }

    [Fact]
    public void GetActiveAlerts_leaves_out_the_alerts_a_silence_covers()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "shop/a", "other/b").Silencing("r", "shop/", TimeSpan.FromHours(1));
        var evaluator = fixture.Build();
        evaluator.Evaluate();

        // Act
        var alerts = evaluator.GetActiveAlerts();

        // Assert
        alerts.Select(a => a.Subject).ShouldBe(["other/b"]);
    }

    [Fact]
    public void GetActiveAlerts_shows_an_alert_again_when_its_silence_ends_and_it_still_holds()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "shop/a").Silencing("r", "", TimeSpan.FromHours(1));
        var evaluator = fixture.Build();
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        evaluator.Evaluate();

        // Act
        var alerts = evaluator.GetActiveAlerts();

        // Assert: it kept counting while silenced
        alerts.Single().Since.ShouldBe(fixture.Clock.GetUtcNow() - TimeSpan.FromHours(1));
    }

    [Fact]
    public void GetActiveAlerts_describes_the_alert_with_a_stable_key_and_the_time_the_condition_began()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("service-down", 0, AlertSeverity.Critical).Failing("service-down", "shop/orders");
        var evaluator = fixture.Build();
        var began = fixture.Clock.GetUtcNow();
        evaluator.Evaluate();

        // Act
        var alert = evaluator.GetActiveAlerts().Single();

        // Assert
        alert.ShouldBe(new Alert("service-down:shop/orders", "service-down", "shop/orders", AlertSeverity.Critical, began));
    }

    [Fact]
    public void Evaluate_keeps_the_original_start_time_while_the_condition_persists()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "a");
        var evaluator = fixture.Build();
        var began = fixture.Clock.GetUtcNow();
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        evaluator.Evaluate();

        // Assert
        evaluator.GetActiveAlerts().Single().Since.ShouldBe(began);
    }

    [Fact]
    public void Evaluate_clears_the_alert_when_the_condition_is_no_longer_true()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "a");
        var evaluator = fixture.Build();
        evaluator.Evaluate();

        // Act
        fixture.Failing("r");
        evaluator.Evaluate();

        // Assert
        evaluator.GetActiveAlerts().ShouldBeEmpty();
    }

    [Fact]
    public void Evaluate_restarts_the_wait_when_a_cleared_condition_returns()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 60).Failing("r", "a");
        var evaluator = fixture.Build();
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromSeconds(50));
        fixture.Failing("r");
        evaluator.Evaluate();
        fixture.Failing("r", "a");
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromSeconds(50));

        // Act
        var alerts = evaluator.GetActiveAlerts();

        // Assert
        alerts.ShouldBeEmpty();
    }

    [Fact]
    public void Evaluate_tracks_each_subject_of_a_rule_separately()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "a", "b");
        var evaluator = fixture.Build();
        evaluator.Evaluate();

        // Act
        fixture.Failing("r", "b");
        evaluator.Evaluate();

        // Assert
        evaluator.GetActiveAlerts().Select(a => a.Subject).ShouldBe(["b"]);
    }

    [Fact]
    public void Evaluate_counts_a_subject_reported_twice_once()
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithRule("r", 0).Failing("r", "a", "a");
        var evaluator = fixture.Build();

        // Act
        evaluator.Evaluate();

        // Assert
        evaluator.GetActiveAlerts().Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    public void Evaluate_does_nothing_during_the_startup_grace_period(int secondsSinceStart, bool evaluated)
    {
        // Arrange
        var fixture = new EvaluatorFixture().WithGrace(TimeSpan.FromMinutes(5)).WithRule("r", 0).Failing("r", "a");
        var evaluator = fixture.Build();
        fixture.Clock.Advance(TimeSpan.FromSeconds(secondsSinceStart));

        // Act
        evaluator.Evaluate();

        // Assert
        evaluator.GetActiveAlerts().Count.ShouldBe(evaluated ? 1 : 0);
    }

    [Fact]
    public void GetActiveAlerts_orders_critical_before_warning_then_by_start_time()
    {
        // Arrange
        var fixture = new EvaluatorFixture()
            .WithRule("warn", 0, AlertSeverity.Warning)
            .WithRule("crit", 0, AlertSeverity.Critical)
            .Failing("warn", "old");
        var evaluator = fixture.Build();
        evaluator.Evaluate();
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Failing("crit", "new");
        evaluator.Evaluate();

        // Act
        var keys = evaluator.GetActiveAlerts().Select(a => a.Key);

        // Assert
        keys.ShouldBe(["crit:new", "warn:old"]);
    }

    /// <summary>Builds an evaluator with a fake clock and rules whose failing subjects the test controls.</summary>
    private sealed class EvaluatorFixture
    {
        private readonly List<FakeRule> _rules = [];
        private readonly List<AlertSilence> _silences = [];
        private TimeSpan _grace = TimeSpan.Zero;

        public FakeTimeProvider Clock { get; } = new();

        /// <summary>Creates the evaluator; its startup grace period begins at the current time of <see cref="Clock"/>.</summary>
        public AlertEvaluator Build() => new(_rules, new AlertingOptions { StartupGrace = _grace, Silences = _silences }, Clock);

        public EvaluatorFixture WithGrace(TimeSpan grace)
        {
            _grace = grace;
            return this;
        }

        public EvaluatorFixture Silencing(string rule, string subjectPrefix, TimeSpan forHowLong)
        {
            _silences.Add(new AlertSilence(rule, subjectPrefix, Clock.GetUtcNow() + forHowLong));
            return this;
        }

        public EvaluatorFixture WithRule(string name, int waitSeconds, AlertSeverity severity = AlertSeverity.Warning)
        {
            _rules.Add(new FakeRule(name, severity, TimeSpan.FromSeconds(waitSeconds)));
            return this;
        }

        /// <summary>Sets the subjects for which the named rule's condition is currently true.</summary>
        public EvaluatorFixture Failing(string rule, params string[] subjects)
        {
            _rules.Single(r => r.Name == rule).Subjects = subjects;
            return this;
        }
    }

    /// <summary>Rule whose failing subjects are set directly by the test.</summary>
    private sealed class FakeRule(string name, AlertSeverity severity, TimeSpan wait) : IAlertRule
    {
        public string Name { get; } = name;

        public AlertSeverity Severity { get; } = severity;

        public TimeSpan Wait { get; } = wait;

        public string[] Subjects { get; set; } = [];

        public IEnumerable<string> FindFailingSubjects() => Subjects;
    }
}
