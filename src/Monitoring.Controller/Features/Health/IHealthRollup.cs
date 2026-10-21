using Monitoring.Protocol.Health;

namespace Monitoring.Controller.Features.Health;

/// <summary>
/// Rolls instance health up to service and scope level as worst-of, from what the registry currently holds.
/// </summary>
/// <remarks>
/// Only live instances count (Starting or Running). Terminating instances are leaving on purpose, for example during
/// a rollout, and Gone instances no longer exist, so neither may turn a service red.
/// </remarks>
public interface IHealthRollup
{
    /// <summary>Gets the health of every service in a scope, as last reported, even when the scope's agent is gone.</summary>
    /// <param name="scopeName">The scope to read.</param>
    /// <returns>One entry per service ordered by name, or an empty list for an unknown scope.</returns>
    IReadOnlyList<ServiceHealth> GetServiceHealth(string scopeName);

    /// <summary>
    /// Gets the health of a scope: the worst health of its services, or Unknown while its agent is not alive, because then
    /// nothing is known about its workloads and the last reported state may be stale.
    /// </summary>
    /// <param name="scopeName">The scope to read.</param>
    /// <returns>The worst service health, or <see cref="HealthState.Unknown"/> when the agent is gone or nothing is known about the scope.</returns>
    HealthState GetScopeHealth(string scopeName);
}
