using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Plover.Controller.Features.Packages;

namespace Plover.Controller.Features.Alerting;

/// <summary>Maps the read API of the rules the controller applies.</summary>
public static class RulesEndpoints
{
    /// <summary>Maps <c>GET /api/rules</c>: the alert rules with their severity and wait, and the packages that must be aligned.</summary>
    /// <param name="endpoints">The route builder to add the endpoint to.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapRules(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/rules", (IEnumerable<IAlertRule> rules, PackagesOptions packages) =>
            Results.Json(
                new RulesReport(
                    [.. rules.Select(rule => new AlertRuleInfo(rule.Name, rule.Severity.ToString(), rule.Wait.TotalSeconds)).OrderBy(rule => rule.Name, StringComparer.Ordinal)],
                    [.. packages.Aligned.Select(rule => new AlignmentRuleInfo(rule.Package, rule.ToString(), rule.Description))]),
                RulesJsonContext.Default.RulesReport));
        return endpoints;
    }
}

/// <summary>The rules in force.</summary>
/// <param name="Alerts">The alert rules.</param>
/// <param name="Aligned">The packages that must be used in the same version.</param>
public sealed record RulesReport(IReadOnlyList<AlertRuleInfo> Alerts, IReadOnlyList<AlignmentRuleInfo> Aligned);

/// <summary>One alert rule.</summary>
/// <param name="Name">The name of the rule, as in the alerts it raises.</param>
/// <param name="Severity">The severity of its alerts.</param>
/// <param name="WaitSeconds">How long a condition must hold before the alert fires.</param>
public sealed record AlertRuleInfo(string Name, string Severity, double WaitSeconds);

/// <summary>One alignment rule.</summary>
/// <param name="Package">The package id, or a prefix ending in <c>*</c>.</param>
/// <param name="Rule">The rule in words.</param>
/// <param name="Description">Why the rule exists, when the configuration says.</param>
public sealed record AlignmentRuleInfo(string Package, string Rule, string? Description);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RulesReport))]
internal sealed partial class RulesJsonContext : JsonSerializerContext;
