namespace Monitoring.Controller.Features.SelfMonitoring;

/// <summary>What the other slices tell about this replica, so the System view can show it.</summary>
public interface ISystemTelemetry
{
    /// <summary>Records that a page opened the live stream.</summary>
    void EventClientConnected();

    /// <summary>Records that a page closed the live stream.</summary>
    void EventClientDisconnected();

    /// <summary>Records how long the latest run of the alert rules took.</summary>
    /// <param name="took">The duration.</param>
    void AlertsEvaluated(TimeSpan took);

    /// <summary>Records that a frame arrived from an agent.</summary>
    void FrameReceived();
}
