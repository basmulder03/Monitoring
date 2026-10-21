using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Agent.Features.ScopeState;

/// <summary>Registers the scope state slice.</summary>
public static class ScopeStateSlice
{
    /// <summary>Adds the in-memory scope state store.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="ScopeStateOptions"/>; the scope name is required.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The scope name is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The subscriber buffer is not positive.</exception>
    public static IServiceCollection AddScopeState(this IServiceCollection services, Action<ScopeStateOptions> configure)
    {
        var options = new ScopeStateOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.ScopeName))
        {
            throw new ArgumentException("The scope name must be set.", nameof(configure));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SubscriberBuffer, 0);

        services.AddSingleton(options);
        services.AddSingleton<IScopeStateStore, ScopeStateStore>();
        return services;
    }
}
