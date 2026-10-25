namespace Plover.Controller.Features.Alerting;

/// <summary>Keeps matching alerts out of the active list until a given time, for example during planned maintenance.</summary>
/// <param name="Rule">Name of the rule to silence, or <c>*</c> for every rule.</param>
/// <param name="SubjectPrefix">Silences the subjects that start with this text, such as <c>shop/</c> for a whole scope; empty matches every subject.</param>
/// <param name="Until">When the silence ends. Required, so a forgotten silence cannot hide alerts for ever.</param>
/// <param name="Reason">Why the silence exists; shown to whoever reads the configuration.</param>
public sealed record AlertSilence(string Rule, string SubjectPrefix, DateTimeOffset Until, string? Reason = null)
{
    /// <summary>Tells whether this silence hides an alert at a moment.</summary>
    /// <param name="ruleName">The rule that raised the alert.</param>
    /// <param name="subject">What the alert is about.</param>
    /// <param name="now">The moment to judge at.</param>
    /// <returns><see langword="true"/> when the silence is still in force and covers the alert.</returns>
    public bool Covers(string ruleName, string subject, DateTimeOffset now) =>
        now < Until
        && (Rule == "*" || Rule == ruleName)
        && subject.StartsWith(SubjectPrefix, StringComparison.Ordinal);
}
