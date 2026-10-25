using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plover.Controller.Features.StateStore;

namespace Plover.Controller.Features.Alerting;

/// <summary>A silence an operator made on the dashboard. Unlike one in the configuration it can be taken back, and it ends at a time that is required.</summary>
/// <param name="Id">Identity of the silence, which the replicas use to agree on it.</param>
/// <param name="Rule">Name of the rule to silence, or <c>*</c> for every rule.</param>
/// <param name="SubjectPrefix">Silences the subjects that start with this text; empty matches every subject.</param>
/// <param name="Until">When the silence ends.</param>
/// <param name="Reason">Why, as the operator wrote it.</param>
/// <param name="By">Who made it.</param>
/// <param name="UpdatedAt">When it was made or taken back; the latest change wins when replicas compare.</param>
/// <param name="Removed">Whether it was taken back. The record stays until its end, so replicas that missed the removal still learn of it.</param>
public sealed record RuntimeSilence(string Id, string Rule, string SubjectPrefix, DateTimeOffset Until, string? Reason, string By, DateTimeOffset UpdatedAt, bool Removed = false)
{
    /// <summary>Tells whether this silence hides an alert at a moment.</summary>
    /// <param name="ruleName">The rule that raised the alert.</param>
    /// <param name="subject">What the alert is about.</param>
    /// <param name="now">The moment to judge at.</param>
    /// <returns><see langword="true"/> when the silence is in force and covers the alert.</returns>
    public bool Covers(string ruleName, string subject, DateTimeOffset now) =>
        !Removed && now < Until && (Rule == "*" || Rule == ruleName) && subject.StartsWith(SubjectPrefix, StringComparison.Ordinal);
}

/// <summary>Someone saying they are looking at an alert, so the others know.</summary>
/// <param name="Key">The key of the alert.</param>
/// <param name="By">Who acknowledged it.</param>
/// <param name="At">When it was acknowledged or taken back; the latest change wins when replicas compare.</param>
/// <param name="Note">What they wrote, if anything.</param>
/// <param name="Removed">Whether it was taken back, or the alert stopped firing.</param>
public sealed record Acknowledgement(string Key, string By, DateTimeOffset At, string? Note, bool Removed = false);

/// <summary>Everything operators did, which is what replicas exchange so each shows the same.</summary>
/// <param name="Silences">The silences, including recent removals.</param>
/// <param name="Acknowledgements">The acknowledgements, including recent removals.</param>
public sealed record ActionsExport(IReadOnlyList<RuntimeSilence> Silences, IReadOnlyList<Acknowledgement> Acknowledgements);

/// <summary>
/// What operators did to alerts on the dashboard: silences and acknowledgements. No agent can tell this again after a restart, so it goes through the state store
/// when there is one that keeps things, and replicas copy it from each other (<see cref="Merge"/>). All members are safe to call from several threads.
/// </summary>
internal sealed class AlertActions
{
    /// <summary>The most silences kept, so the list cannot grow without end.</summary>
    public const int MaxSilences = 500;

    /// <summary>The most acknowledgements kept.</summary>
    public const int MaxAcknowledgements = 5000;

    /// <summary>The longest a silence may last, so a forgotten one cannot hide alerts for ever.</summary>
    public static readonly TimeSpan MaxSilence = TimeSpan.FromDays(31);

    private const string Log = "alert-actions";
    private static readonly TimeSpan KeepRemoved = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly IStateStore? _store;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, RuntimeSilence> _silences = [];
    private readonly Dictionary<string, Acknowledgement> _acknowledgements = new(StringComparer.Ordinal);

    /// <summary>Creates the holder and reads what the state store kept.</summary>
    /// <param name="time">Clock for the times of changes.</param>
    /// <param name="store">Keeps the actions across a restart; optional.</param>
    /// <param name="logger">Receives who did what.</param>
    public AlertActions(TimeProvider time, IStateStore? store = null, ILogger? logger = null)
    {
        _time = time;
        _store = store;
        _logger = logger;
        if (store?.Read(Log).LastOrDefault() is { } json)
        {
            try
            {
                if (JsonSerializer.Deserialize(json, AlertActionsJsonContext.Default.ActionsExport) is { } kept)
                {
                    MergeCore(kept);
                }
            }
            catch (JsonException ex)
            {
                logger?.LogWarning("The alert actions that were kept cannot be read and are dropped: {Reason}", ex.Message);
            }
        }
    }

    /// <summary>Silences alerts.</summary>
    /// <param name="rule">The rule to silence, or <c>*</c>.</param>
    /// <param name="subjectPrefix">What the subject has to start with.</param>
    /// <param name="until">When the silence ends.</param>
    /// <param name="reason">Why.</param>
    /// <param name="by">Who.</param>
    /// <returns>The silence.</returns>
    /// <exception cref="ArgumentException">The rule is blank, the end is not in the future or is more than <see cref="MaxSilence"/> away, or there are too many silences.</exception>
    public RuntimeSilence Silence(string rule, string subjectPrefix, DateTimeOffset until, string? reason, string by)
    {
        var now = _time.GetUtcNow();
        if (string.IsNullOrWhiteSpace(rule))
        {
            throw new ArgumentException("A silence needs the name of a rule, or '*' for every rule.", nameof(rule));
        }

        if (until <= now || until - now > MaxSilence)
        {
            throw new ArgumentException($"A silence must end in the future and within {MaxSilence.TotalDays:0} days.", nameof(until));
        }

        lock (_gate)
        {
            Prune(now);
            if (_silences.Count >= MaxSilences)
            {
                throw new ArgumentException("There are too many silences; take some back first.", nameof(rule));
            }

            var silence = new RuntimeSilence(Guid.NewGuid().ToString("N"), rule.Trim(), subjectPrefix ?? "", until, Clean(reason), by, now);
            _silences[silence.Id] = silence;
            Save();
            _logger?.LogInformation("{User} silenced rule '{Rule}' for subjects starting with '{Subject}' until {Until:u}: {Reason}", by, silence.Rule, silence.SubjectPrefix, until, silence.Reason);
            return silence;
        }
    }

    /// <summary>Takes a silence back.</summary>
    /// <param name="id">The id of the silence.</param>
    /// <param name="by">Who.</param>
    /// <returns><see langword="false"/> when there is no such silence in force.</returns>
    public bool RemoveSilence(string id, string by)
    {
        lock (_gate)
        {
            if (!_silences.TryGetValue(id, out var silence) || silence.Removed || silence.Until <= _time.GetUtcNow())
            {
                return false;
            }

            _silences[id] = silence with { Removed = true, UpdatedAt = _time.GetUtcNow() };
            Save();
            _logger?.LogInformation("{User} took back the silence of rule '{Rule}' for '{Subject}'", by, silence.Rule, silence.SubjectPrefix);
            return true;
        }
    }

    /// <summary>Acknowledges an alert.</summary>
    /// <param name="key">The key of the alert.</param>
    /// <param name="by">Who.</param>
    /// <param name="note">What they wrote.</param>
    /// <returns>The acknowledgement.</returns>
    /// <exception cref="ArgumentException">The key is blank, or there are too many acknowledgements.</exception>
    public Acknowledgement Acknowledge(string key, string by, string? note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            Prune(_time.GetUtcNow());
            if (_acknowledgements.Count >= MaxAcknowledgements && !_acknowledgements.ContainsKey(key))
            {
                throw new ArgumentException("There are too many acknowledgements.", nameof(key));
            }

            var ack = new Acknowledgement(key, by, _time.GetUtcNow(), Clean(note));
            _acknowledgements[key] = ack;
            Save();
            _logger?.LogInformation("{User} acknowledged alert '{Key}': {Note}", by, key, ack.Note);
            return ack;
        }
    }

    /// <summary>Takes an acknowledgement back.</summary>
    /// <param name="key">The key of the alert.</param>
    /// <param name="by">Who.</param>
    /// <returns><see langword="false"/> when the alert was not acknowledged.</returns>
    public bool RemoveAcknowledgement(string key, string by)
    {
        lock (_gate)
        {
            if (!_acknowledgements.TryGetValue(key, out var ack) || ack.Removed)
            {
                return false;
            }

            _acknowledgements[key] = ack with { Removed = true, At = _time.GetUtcNow() };
            Save();
            _logger?.LogInformation("{User} took back the acknowledgement of alert '{Key}'", by, key);
            return true;
        }
    }

    /// <summary>The silences in force, the ones that end first at the top.</summary>
    /// <returns>The silences.</returns>
    public IReadOnlyList<RuntimeSilence> Silences()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return [.. _silences.Values.Where(s => !s.Removed && s.Until > now).OrderBy(s => s.Until).ThenBy(s => s.Id, StringComparer.Ordinal)];
        }
    }

    /// <summary>Tells whether a silence of an operator hides an alert.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="now">The moment to judge at.</param>
    /// <returns><see langword="true"/> when one covers it.</returns>
    public bool Silenced(string rule, string subject, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _silences.Values.Any(s => s.Covers(rule, subject, now));
        }
    }

    /// <summary>Who acknowledged an alert.</summary>
    /// <param name="key">The key of the alert.</param>
    /// <returns>The acknowledgement, or <see langword="null"/> when there is none.</returns>
    public Acknowledgement? AcknowledgementOf(string key)
    {
        lock (_gate)
        {
            return _acknowledgements.TryGetValue(key, out var ack) && !ack.Removed ? ack : null;
        }
    }

    /// <summary>Drops the acknowledgements of alerts that no longer fire, because an acknowledgement belongs to one occurrence of a problem.</summary>
    /// <param name="liveKeys">The keys of the conditions that are true now.</param>
    public void ForgetAcknowledgementsNotIn(IReadOnlySet<string> liveKeys)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var (key, ack) in _acknowledgements.Where(p => !p.Value.Removed && !liveKeys.Contains(p.Key)).ToList())
            {
                _acknowledgements[key] = ack with { Removed = true, At = _time.GetUtcNow() };
                changed = true;
            }

            if (changed)
            {
                Save();
            }
        }
    }

    /// <summary>What replicas copy from each other.</summary>
    /// <returns>All silences and acknowledgements, including recent removals.</returns>
    public ActionsExport Export()
    {
        lock (_gate)
        {
            Prune(_time.GetUtcNow());
            return new ActionsExport([.. _silences.Values], [.. _acknowledgements.Values]);
        }
    }

    /// <summary>Takes over what another replica knows; the latest change of each silence and acknowledgement wins.</summary>
    /// <param name="other">What the other replica sent.</param>
    public void Merge(ActionsExport other)
    {
        lock (_gate)
        {
            if (MergeCore(other))
            {
                Save();
            }
        }
    }

    private bool MergeCore(ActionsExport other)
    {
        var changed = false;
        var now = _time.GetUtcNow();
        foreach (var silence in other.Silences.Take(MaxSilences))
        {
            if (silence.Until <= now - KeepRemoved || string.IsNullOrWhiteSpace(silence.Id) || (silence.Until - now) > MaxSilence)
            {
                continue;
            }

            if (!_silences.TryGetValue(silence.Id, out var known) || silence.UpdatedAt > known.UpdatedAt)
            {
                _silences[silence.Id] = silence;
                changed = true;
            }
        }

        foreach (var ack in other.Acknowledgements.Take(MaxAcknowledgements))
        {
            if (string.IsNullOrWhiteSpace(ack.Key) || (ack.Removed && ack.At <= now - KeepRemoved))
            {
                continue;
            }

            if (!_acknowledgements.TryGetValue(ack.Key, out var known) || ack.At > known.At)
            {
                _acknowledgements[ack.Key] = ack;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Drops what has ended and what was taken back long enough ago for every replica to have heard of it.</summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _silences.Where(p => p.Value.Until <= now - KeepRemoved).Select(p => p.Key).ToList())
        {
            _silences.Remove(id);
        }

        foreach (var key in _acknowledgements.Where(p => p.Value.Removed && p.Value.At <= now - KeepRemoved).Select(p => p.Key).ToList())
        {
            _acknowledgements.Remove(key);
        }
    }

    private void Save() =>
        _store?.Append(Log, JsonSerializer.Serialize(new ActionsExport([.. _silences.Values], [.. _acknowledgements.Values]), AlertActionsJsonContext.Default.ActionsExport), keep: 1);

    /// <summary>What an operator typed is kept short and on one line, because it ends up in logs and in channels.</summary>
    private static string? Clean(string? text)
    {
        var line = text?.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrEmpty(line) ? null : line.Length > 200 ? line[..200] : line;
    }
}
