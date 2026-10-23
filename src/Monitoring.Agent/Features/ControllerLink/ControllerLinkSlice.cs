using Monitoring.Agent.Features.Configuration;
using Monitoring.Agent.Features.SelfMonitoring;
using Monitoring.Common;
using Monitoring.Common.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Registers the controller link slice.</summary>
public static class ControllerLinkSlice
{
    /// <summary>Adds the link to the controller replicas and the background service that runs it. It sends the scope state and hands what the controller says to the agent's configuration, so <c>AddScopeState</c> and <c>AddAgentConfiguration</c> must be called as well.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="ControllerLinkOptions"/>; at least one controller address and a token are required.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">There is no controller address, an address is not <c>host:port</c>, or the token is missing.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A delay or limit is out of range.</exception>
    public static IServiceCollection AddControllerLink(this IServiceCollection services, Action<ControllerLinkOptions> configure)
    {
        var options = new ControllerLinkOptions();
        configure(options);
        Validate(options);

        services.AddLogging();
        services.AddTransportSecurity(options.Transport, server: false);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<ControllerSession>();
        services.AddSingleton<ConnectAsync>(provider =>
            new SecureConnector(provider.GetRequiredService<ITransportSecurity>(), Monitoring.Common.TcpConnector.ConnectAsync).ConnectAsync);
        services.AddSingleton<RunSessionAsync>(provider =>
        {
            var session = provider.GetRequiredService<ControllerSession>();
            return (replica, stream, onConfig, ct) => session.RunAsync(replica, stream, onConfig, ct);
        });
        services.AddSingleton(provider => new ControllerConnection(
            provider.GetRequiredService<ConnectAsync>(),
            provider.GetRequiredService<RunSessionAsync>(),
            options,
            provider.GetRequiredService<TimeProvider>(),
            Random.Shared.NextDouble,
            provider.GetRequiredService<ILogger<ControllerConnection>>(),
            provider.GetService<IAgentTelemetry>()));
        services.AddSingleton<RunReplicaAsync>(provider =>
        {
            var connection = provider.GetRequiredService<ControllerConnection>();
            var configuration = provider.GetRequiredService<IAgentConfiguration>();

            // Every Config a replica sends is the configuration of the agent first, and only then the business of the link.
            return (address, onConfig, ct) => connection.RunAsync(address, config =>
            {
                configuration.Apply(config);
                onConfig(config);
            }, ct);
        });
        services.AddSingleton<IControllerLink, ControllerLinkService>();
        services.AddHostedService<ControllerLinkHostedService>();
        return services;
    }

    private static void Validate(ControllerLinkOptions options)
    {
        if (options.ControllerAddresses.Count == 0)
        {
            throw new ArgumentException("At least one controller address is required.", nameof(options));
        }

        foreach (var address in options.ControllerAddresses.Where(a => !HostPort.TryParse(a, out _, out _)))
        {
            throw new ArgumentException($"'{address}' is not a host:port address.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            throw new ArgumentException("A token is required to authenticate with the controller.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReconnectMinDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReconnectMaxDelay, options.ReconnectMinDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ConfigReplyTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SnapshotInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxFrameBytes, 0);
    }
}
