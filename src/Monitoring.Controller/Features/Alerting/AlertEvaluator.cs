namespace Monitoring.Controller.Features.Alerting;

/// <summary>
/// Default <see cref="IAlertEvaluator"/> and <see cref="IAlertState"/>: tracks since when each condition has been
/// true and reports it as an alert once its rule's wait has passed.
/// </summary>
/// <remarks>
/// The grace period starts when the evaluator is created, which is at controller startup. Because nothing is
/// persisted, a restarted replica would otherwise raise alerts for everything its agents have not yet re-sent.
/// All members are safe to call from several threads.
/// </remarks>
internal sealed class AlertEvaluator : IAlertEvaluator, IAlertState
{
    private readonly Lock _gate = new();
    private readonly IReadOnlyList<IAlertRule> _rules;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _graceEnds;
    private readonly IReadOnlyList<AlertSilence> _silences;
    private readonly AlertActions? _actions;
    private readonly AlertHistory? _history;

    /// <summary>Conditions currently true, by alert key; guarded by <see cref="_gate"/>.</summary>
    private Dictionary<string, Condition> _conditions = [];

    /// <summary>Creates an evaluator whose startup grace period begins now.</summary>
    /// <param name="rules">The rules to evaluate.</param>
    /// <param name="options">Provides the startup grace period.</param>
    /// <param name="time">Clock used for grace and wait times.</param>
    /// <param name="actions">The silences and acknowledgements of operators; without it only the silences of the configuration apply.</param>
    /// <param name="history">Told what starts and stops firing; optional.</param>
    public AlertEvaluator(IEnumerable<IAlertRule> rules, AlertingOptions options, TimeProvider time, AlertActions? actions = null, AlertHistory? history = null)
    {
        _actions = actions;
        _history = history;
        _rules = [.. rules];
        _time = time;
        _silences = [.. options.Silences];
        _graceEnds = time.GetUtcNow() + options.StartupGrace;
    }

    /// <inheritdoc />
    public void Evaluate()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now < _graceEnds)
            {
                return;
            }

            // Rebuilding the map drops every condition that is no longer true, so a cleared problem that comes
            // back later starts its wait from zero again.
            var current = new Dictionary<string, Condition>();
            foreach (var rule in _rules)
            {
                foreach (var subject in rule.FindFailingSubjects().Distinct())
                {
                    var key = KeyOf(rule, subject);
                    current[key] = _conditions.TryGetValue(key, out var known)
                        ? known
                        : new Condition(rule, subject, now);
                }
            }

            _conditions = current;
            _actions?.ForgetAcknowledgementsNotIn(current.Keys.ToHashSet(StringComparer.Ordinal));
            _history?.Update(current.Where(pair => now - pair.Value.Since >= pair.Value.Rule.Wait).ToDictionary(
                pair => pair.Key, pair => new Alert(pair.Key, pair.Value.Rule.Name, pair.Value.Subject, pair.Value.Rule.Severity, pair.Value.Since), StringComparer.Ordinal), now);
        }
    }

    /// <inheritdoc />
    public bool IsReady => _time.GetUtcNow() >= _graceEnds;

    /// <inheritdoc />
    public IReadOnlyList<Alert> GetActiveAlerts()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return
            [
                .. _conditions
                    .Where(pair => now - pair.Value.Since >= pair.Value.Rule.Wait)
                    .Where(pair => !_silences.Any(silence => silence.Covers(pair.Value.Rule.Name, pair.Value.Subject, now)))
                    .Where(pair => _actions?.Silenced(pair.Value.Rule.Name, pair.Value.Subject, now) != true)
                    .Select(pair => new Alert(pair.Key, pair.Value.Rule.Name, pair.Value.Subject, pair.Value.Rule.Severity, pair.Value.Since, _actions?.AcknowledgementOf(pair.Key)))
                    .OrderByDescending(alert => alert.Severity)
                    .ThenBy(alert => alert.Since)
                    .ThenBy(alert => alert.Key, StringComparer.Ordinal),
            ];
        }
    }

    private static string KeyOf(IAlertRule rule, string subject) => $"{rule.Name}:{subject}";

    /// <summary>A rule's condition that is true for a subject, and since when.</summary>
    private sealed record Condition(IAlertRule Rule, string Subject, DateTimeOffset Since);
}
