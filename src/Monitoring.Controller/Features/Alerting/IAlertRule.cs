namespace Monitoring.Controller.Features.Alerting;

/// <summary>A state-based condition: "is this true for N minutes". Rules keep no state of their own, except what they need to see something change over time.</summary>
internal interface IAlertRule
{
    /// <summary>Unique, stable rule name; part of every alert key.</summary>
    string Name { get; }

    /// <summary>Severity of the alerts this rule raises.</summary>
    AlertSeverity Severity { get; }

    /// <summary>How long a subject must keep failing before its alert fires; zero fires at once.</summary>
    TimeSpan Wait { get; }

    /// <summary>Finds the subjects for which the condition is true right now.</summary>
    /// <returns>Distinct subject identifiers.</returns>
    IEnumerable<string> FindFailingSubjects();
}
