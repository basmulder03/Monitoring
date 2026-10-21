using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Monitoring.Common.Transport;

/// <summary>Registers the transport security of an executable.</summary>
public static class TransportSecurityRegistration
{
    /// <summary>
    /// Creates the transport security from the settings, so a missing or unreadable certificate stops the executable from starting,
    /// and registers it. Without encryption a warning is logged once at startup.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="options">The settings.</param>
    /// <param name="server">Whether this is the controller side, which accepts connections.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">A setting that the mode needs is missing, or a certificate file cannot be read or holds no certificate.</exception>
    public static IServiceCollection AddTransportSecurity(this IServiceCollection services, TransportSecurityOptions options, bool server)
    {
        var security = server ? TransportSecurity.ForServer(options) : TransportSecurity.ForClient(options);
        services.AddSingleton(security);
        services.AddSingleton<IHostedService>(provider => new PlainTransportWarning(security, provider.GetRequiredService<ILogger<PlainTransportWarning>>()));
        return services;
    }

    /// <summary>Warns at startup that connections are not encrypted.</summary>
    private sealed class PlainTransportWarning(ITransportSecurity security, ILogger<PlainTransportWarning> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!security.IsEncrypted)
            {
                logger.LogWarning("Connections between agents and controllers are not encrypted, so agent tokens travel in clear text. Use this only on a network you trust, or turn on TLS.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
