namespace Monitoring.Controller.Features.Alerting;

/// <summary>An alert that is currently firing.</summary>
/// <param name="Key">
/// Stable identity derived from the rule and the subject, so a repeated notification after a restart
/// can be recognised by the receiving system.
/// </param>
/// <param name="RuleName">Name of the rule that fired.</param>
/// <param name="Subject">What the alert is about, for example <c>shop/orders</c> for a service in a scope.</param>
/// <param name="Severity">How urgent the alert is.</param>
/// <param name="Since">When the condition first became true, so the UI can show how long the problem has lasted.</param>
/// <param name="Acknowledged">Who said they are looking at it, or <see langword="null"/> when nobody did.</param>
public sealed record Alert(string Key, string RuleName, string Subject, AlertSeverity Severity, DateTimeOffset Since, Acknowledgement? Acknowledged = null);
