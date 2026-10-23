namespace Monitoring.Controller.Features.Alerting;

/// <summary>Read access to the alerts that are firing, for the dashboard and later alert channels.</summary>
public interface IAlertState
{
    /// <summary>Gets the alerts that are firing now.</summary>
    /// <returns>The alerts, most severe first, then longest firing first.</returns>
    IReadOnlyList<Alert> GetActiveAlerts();

    /// <summary>Whether the alerts are judged yet. During the startup grace period nothing is, so an empty list means nothing is known and not that nothing is wrong.</summary>
    bool IsReady => true;
}
