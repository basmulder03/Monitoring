using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for a certificate of a service that expires within a number of days, as reported by the application in <c>monitoring.certificate.expiry_days</c>.</summary>
/// <remarks>
/// The warning rule and the critical rule split the days between them (see <see cref="AlertingOptions"/>), and a certificate that has already expired is critical, so one certificate never raises both. Replicas of a service usually share a certificate,
/// so the one that expires first decides.
/// </remarks>
/// <param name="name">The name of the rule.</param>
/// <param name="severity">Severity of the alerts.</param>
/// <param name="registry">Source of the instances.</param>
/// <param name="belowDays">Alerts for certificates with fewer days left than this.</param>
/// <param name="notBelowDays">Leaves out certificates with fewer days left than this, because a stricter rule has them; <see langword="null"/> for none.</param>
internal sealed class CertificateExpiryRule(string name, AlertSeverity severity, IScopeRegistry registry, int belowDays, int? notBelowDays) : IAlertRule
{
    /// <summary>The metric the client reports for each certificate it watches.</summary>
    public const string MetricName = "monitoring.certificate.expiry_days";

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public AlertSeverity Severity => severity;

    /// <inheritdoc />
    public TimeSpan Wait => TimeSpan.Zero;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var soonest = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle != InstanceLifecycle.Gone && i.Metrics is not null))
            {
                foreach (var series in instance.Metrics!.Where(m => m.Name == MetricName))
                {
                    var subject = $"{scope}/{instance.ServiceName}/{series.Labels.GetValueOrDefault("certificate", "certificate")}";
                    soonest[subject] = soonest.TryGetValue(subject, out var known) ? Math.Min(known, series.Value) : series.Value;
                }
            }
        }

        return soonest.Where(pair => pair.Value < belowDays && (notBelowDays is null || pair.Value >= notBelowDays)).Select(pair => pair.Key);
    }
}
