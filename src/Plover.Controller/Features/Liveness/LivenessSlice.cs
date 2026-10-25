using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Plover.Controller.Features.Liveness;

/// <summary>Registers the liveness slice.</summary>
public static class LivenessSlice
{
    /// <summary>Adds the tracking of which agents are alive.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="LivenessOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The heartbeat interval is not positive or fewer than one heartbeat may be missed.</exception>
    public static IServiceCollection AddLiveness(this IServiceCollection services, Action<LivenessOptions>? configure = null)
    {
        var options = new LivenessOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HeartbeatInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MissedHeartbeats, 1);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<IAgentLiveness, AgentLiveness>();
        return services;
    }
}
