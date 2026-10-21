namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Everything the overview page shows: a tile per scope, unhealthiest first, and the alerts that are firing.</summary>
/// <param name="Scopes">The scopes, most severe first, then by name.</param>
/// <param name="Alerts">The alerts that are firing, most severe first.</param>
/// <param name="GroupBy">The label names the page groups scopes by, outermost first, for example <c>environment</c> then <c>datacenter</c>.</param>
public sealed record Overview(IReadOnlyList<ScopeSummary> Scopes, IReadOnlyList<AlertSummary> Alerts, IReadOnlyList<string> GroupBy);
