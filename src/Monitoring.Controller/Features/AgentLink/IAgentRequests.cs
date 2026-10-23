namespace Monitoring.Controller.Features.AgentLink;

/// <summary>Asks the agent of a scope for something that is not part of the state it keeps sending, over the connection it already has.</summary>
public interface IAgentRequests
{
    /// <summary>Asks for the recent values of the metrics of an instance.</summary>
    /// <param name="scopeName">The scope of the instance.</param>
    /// <param name="instanceId">The instance, which is the pod name.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>A <c>MetricHistory</c> as JSON; <see langword="null"/> when the scope has no connected agent that can answer, the agent keeps nothing for the instance, or it did not answer in time.</returns>
    Task<string?> GetMetricHistoryAsync(string scopeName, string instanceId, CancellationToken ct);
}
