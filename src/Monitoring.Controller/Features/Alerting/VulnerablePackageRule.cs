using Monitoring.Controller.Features.Vulnerabilities;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for a package version in use that an advisory says is vulnerable.</summary>
/// <remarks>Two instances exist, one per severity, because a rule has one severity: high and critical advisories are critical, the rest are warnings.</remarks>
/// <param name="name">The name of the rule.</param>
/// <param name="severity">How urgent the alerts of this rule are.</param>
/// <param name="serious">Whether this rule is the one for high and critical advisories.</param>
/// <param name="vulnerabilities">Finds the vulnerable versions in use.</param>
/// <param name="options">Provides the wait time.</param>
internal sealed class VulnerablePackageRule(
    string name, AlertSeverity severity, bool serious, IVulnerabilities vulnerabilities, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public AlertSeverity Severity => severity;

    /// <inheritdoc />
    public TimeSpan Wait => options.VulnerablePackageWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects() => vulnerabilities.Check().Findings
        .Where(finding => (finding.Severity is "HIGH" or "CRITICAL") == serious)
        .Select(finding => $"{finding.Advisory} {finding.Package}@{finding.Version}");
}
