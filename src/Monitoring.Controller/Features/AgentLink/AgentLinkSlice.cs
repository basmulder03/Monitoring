using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>Registers the agent link slice.</summary>
public static class AgentLinkSlice
{
    /// <summary>
    /// Adds the listener for agent connections and the background service that runs it. It needs the registry and an <see cref="Security.IAgentAuthenticator"/>,
    /// so <c>AddRegistry</c> and <c>AddSecurity</c> must be called as well.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="AgentLinkOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The listen address is not an IP address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port, timeout or frame limit is out of range.</exception>
    public static IServiceCollection AddAgentLink(this IServiceCollection services, Action<AgentLinkOptions>? configure = null)
    {
        var options = new AgentLinkOptions();
        configure?.Invoke(options);
        if (!IPAddress.TryParse(options.ListenAddress, out _))
        {
            throw new ArgumentException($"'{options.ListenAddress}' is not an IP address.", nameof(configure));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Port, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Port, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.HelloTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxFrameBytes, 0);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<AgentConnectionHandler>();
        services.AddSingleton<IAgentListener, AgentListener>();
        services.AddHostedService<AgentListenerService>();
        return services;
    }
}
