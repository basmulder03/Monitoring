using Monitoring.Controller.Features.Alerting;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>An alert that is firing, as the dashboard shows it.</summary>
/// <param name="Key">Stable identity of the alert.</param>
/// <param name="Rule">Name of the rule that fired.</param>
/// <param name="Subject">What the alert is about, for example <c>shop/orders</c>.</param>
/// <param name="Severity">How urgent the alert is.</param>
/// <param name="Since">When the condition began.</param>
public sealed record AlertSummary(string Key, string Rule, string Subject, AlertSeverity Severity, DateTimeOffset Since);
