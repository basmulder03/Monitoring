using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Registers the controller link slice.</summary>
public static class ControllerLinkSlice
{
    /// <summary>Adds the link to the controller replicas. It sends the scope state, so <c>AddScopeState</c> must be called as well.</summary>
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<ControllerSession>();
        services.AddSingleton<ConnectAsync>(TcpConnector.ConnectAsync);
        services.AddSingleton<RunSessionAsync>(provider =>
        {
            var session = provider.GetRequiredService<ControllerSession>();
            return session.RunAsync;
        });
        services.AddSingleton(provider => new ControllerConnection(
            provider.GetRequiredService<ConnectAsync>(),
            provider.GetRequiredService<RunSessionAsync>(),
            options,
            provider.GetRequiredService<TimeProvider>(),
            Random.Shared.NextDouble,
            provider.GetRequiredService<ILogger<ControllerConnection>>()));
        services.AddSingleton<RunReplicaAsync>(provider => provider.GetRequiredService<ControllerConnection>().RunAsync);
        services.AddSingleton<IControllerLink, ControllerLinkService>();
        return services;
    }

    private static void Validate(ControllerLinkOptions options)
    {
        if (options.ControllerAddresses.Count == 0)
        {
            throw new ArgumentException("At least one controller address is required.", nameof(options));
        }

        foreach (var address in options.ControllerAddresses.Where(a => !ControllerAddress.TryParse(a, out _, out _)))
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
