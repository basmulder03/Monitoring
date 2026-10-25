namespace Plover.Controller.Features.Alerting;

/// <summary>Drives the alert rules; called periodically by the host.</summary>
public interface IAlertEvaluator
{
    /// <summary>
    /// Runs every rule once against the current state, starts tracking new conditions and forgets cleared ones.
    /// Does nothing during the startup grace period.
    /// </summary>
    void Evaluate();
}
