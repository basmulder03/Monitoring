namespace Plover.Agent.Features.ControllerLink;

/// <summary>Keeps the agent connected to every controller replica and feeds each one the scope's state.</summary>
public interface IControllerLink
{
    /// <summary>Connects to the controller replicas and keeps reconnecting until cancelled.</summary>
    /// <param name="ct">Stops the link; open connections say goodbye and close.</param>
    /// <returns>A task that completes once every connection has ended.</returns>
    Task RunAsync(CancellationToken ct);
}
