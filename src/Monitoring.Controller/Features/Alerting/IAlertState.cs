namespace Monitoring.Controller.Features.Alerting;

/// <summary>Read access to the alerts that are firing, for the dashboard and later alert channels.</summary>
public interface IAlertState
{
    /// <summary>Gets the alerts that are firing now.</summary>
    /// <returns>The alerts, most severe first, then longest firing first.</returns>
    IReadOnlyList<Alert> GetActiveAlerts();
}
