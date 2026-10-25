using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Plover.Common;

namespace Plover.Sidecar.Features.AgentLink;

/// <summary>Registers the agent link slice.</summary>
public static class AgentLinkSlice
{
    /// <summary>Adds the registration with the agent and the heartbeat. It reads the application's state, so <c>AddProbing</c> must be called as well.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="AgentLinkOptions"/>; the agent's address is required.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The agent's address is not <c>host:port</c> or the instance id is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A delay or limit is out of range.</exception>
    public static IServiceCollection AddAgentLink(this IServiceCollection services, Action<AgentLinkOptions> configure)
    {
        var options = new AgentLinkOptions();
        configure(options);
        if (!HostPort.TryParse(options.AgentAddress, out _, out _))
        {
            throw new ArgumentException($"'{options.AgentAddress}' is not a host:port address.", nameof(configure));
        }

        if (string.IsNullOrWhiteSpace(options.InstanceId))
        {
            throw new ArgumentException("The instance id must be set.", nameof(configure));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReconnectMinDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReconnectMaxDelay, options.ReconnectMinDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ConfigReplyTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxFrameBytes, 0);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<AgentSession>();
        services.AddSingleton<ConnectAsync>(TcpConnector.ConnectAsync);
        services.AddSingleton<Func<double>>(Random.Shared.NextDouble);
        services.AddHostedService<SidecarService>();
        return services;
    }
}
