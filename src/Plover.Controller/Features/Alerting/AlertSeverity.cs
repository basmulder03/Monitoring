namespace Plover.Controller.Features.Alerting;

/// <summary>How urgent an alert is.</summary>
public enum AlertSeverity : byte
{
    /// <summary>Something is wrong but the service still works.</summary>
    Warning,

    /// <summary>A service is down.</summary>
    Critical,
}
