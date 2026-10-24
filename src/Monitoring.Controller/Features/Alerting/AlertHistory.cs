using System.Text.Json;
using Microsoft.Extensions.Logging;
using Monitoring.Controller.Features.StateStore;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>One time an alert fired: when it started and, once it stopped, when and why.</summary>
/// <param name="Key">The key of the alert.</param>
/// <param name="Rule">The rule that fired.</param>
/// <param name="Subject">What it was about.</param>
/// <param name="Severity">How urgent it was.</param>
/// <param name="Started">When the alert began to fire, which is after the wait of its rule.</param>
/// <param name="Ended">When the condition stopped holding, or <see langword="null"/> while it still does.</param>
/// <param name="EndedBy"><c>cleared</c> when the condition stopped holding, <c>restart</c> when the controller restarted and cannot say, <see langword="null"/> while it still holds.</param>
public sealed record AlertOccurrence(string Key, string Rule, string Subject, AlertSeverity Severity, DateTimeOffset Started, DateTimeOffset? Ended, string? EndedBy);

/// <summary>
/// What fired and when, so an alert that came and went at night can still be seen in the morning. It holds the last <see cref="Max"/> occurrences and keeps them through the
/// state store when that keeps things. Each replica holds its own: they judge the same data, so they tell nearly the same story, but a replica that restarted has less of it.
/// </summary>
internal sealed class AlertHistory
{
    /// <summary>The most occurrences kept.</summary>
    public const int Max = 200;

    private const string Log = "alert-history";

    private readonly Lock _gate = new();
    private readonly IStateStore? _store;
    private List<AlertOccurrence> _items = [];

    /// <summary>Creates the history and reads what the state store kept; what was still firing then is closed, because nobody watched it end.</summary>
    /// <param name="time">Clock for closing what was open.</param>
    /// <param name="store">Keeps the history across a restart; optional.</param>
    /// <param name="logger">Receives what cannot be read.</param>
    public AlertHistory(TimeProvider time, IStateStore? store = null, ILogger? logger = null)
    {
        _store = store;
        if (store?.Read(Log).LastOrDefault() is not { } json)
        {
            return;
        }

        try
        {
            var kept = JsonSerializer.Deserialize(json, AlertActionsJsonContext.Default.ListAlertOccurrence) ?? [];
            var now = time.GetUtcNow();
            _items = [.. kept.Select(o => o.Ended is null ? o with { Ended = now, EndedBy = "restart" } : o).TakeLast(Max)];
        }
        catch (JsonException ex)
        {
            logger?.LogWarning("The alert history that was kept cannot be read and is dropped: {Reason}", ex.Message);
        }
    }

    /// <summary>Brings the history in line with what fires now: what began is opened, what stopped is closed.</summary>
    /// <param name="firing">The alerts whose wait has passed, silenced or not, by key.</param>
    /// <param name="now">The moment.</param>
    public void Update(IReadOnlyDictionary<string, Alert> firing, DateTimeOffset now)
    {
        lock (_gate)
        {
            var changed = false;
            var open = _items.Where(o => o.Ended is null).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Ended is null && !firing.ContainsKey(_items[i].Key))
                {
                    _items[i] = _items[i] with { Ended = now, EndedBy = "cleared" };
                    changed = true;
                }
            }

            foreach (var alert in firing.Values.Where(a => !open.Contains(a.Key)))
            {
                _items.Add(new AlertOccurrence(alert.Key, alert.RuleName, alert.Subject, alert.Severity, now, null, null));
                changed = true;
            }

            if (_items.Count > Max)
            {
                _items = [.. _items.TakeLast(Max)];
            }

            if (changed)
            {
                _store?.Append(Log, JsonSerializer.Serialize(_items, AlertActionsJsonContext.Default.ListAlertOccurrence), keep: 1);
            }
        }
    }

    /// <summary>The occurrences, the latest first.</summary>
    /// <param name="limit">How many at most.</param>
    /// <returns>The occurrences.</returns>
    public IReadOnlyList<AlertOccurrence> Recent(int limit)
    {
        lock (_gate)
        {
            return [.. Enumerable.Reverse(_items).Take(Math.Clamp(limit, 1, Max))];
        }
    }
}
