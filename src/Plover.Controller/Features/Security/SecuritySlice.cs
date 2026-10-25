using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Security;

/// <summary>Registers the security slice.</summary>
public static class SecuritySlice
{
    private const int Sha256HexLength = 64;

    /// <summary>Adds token authentication for agents.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the accepted tokens.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">An entry has no scope name or its hash is not 64 hexadecimal characters.</exception>
    public static IServiceCollection AddSecurity(this IServiceCollection services, Action<SecurityOptions> configure)
    {
        var options = new SecurityOptions();
        configure(options);
        foreach (var entry in options.AgentTokens)
        {
            Validate(entry);
        }

        services.AddSingleton(options);
        services.AddSingleton<IHostedService>(provider => new EnrollmentTokenWarning(options, provider.GetRequiredService<ILoggerFactory>().CreateLogger("Plover.Controller.Security")));
        if (options.Certificates.Enabled)
        {
            services.AddSingleton<ICertificateIdentity>(new CertificateAgentIdentity(options.Certificates));
        }

        if (options.ServiceAccounts.Enabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ServiceAccounts.Audience, nameof(configure));
            var accounts = options.ServiceAccounts;
            var client = KubernetesTokenReviewer.CreateClient(accounts, Environment.GetEnvironmentVariable);
            services.AddSingleton(accounts);
            services.TryAddSingleton<IAgentAuthenticator>(provider => new CompositeAgentAuthenticator(
                new TokenAgentAuthenticator(options), new ServiceAccountAgentAuthenticator(new KubernetesTokenReviewer(client), accounts, provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Plover.Controller.Security"))));
        }
        else
        {
            services.TryAddSingleton<IAgentAuthenticator, TokenAgentAuthenticator>();
        }

        return services;
    }

    private static void Validate(AgentTokenEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ScopeName))
        {
            throw new ArgumentException("An agent token entry needs a scope name.", nameof(entry));
        }

        var isHash = entry.TokenHash is { Length: Sha256HexLength } hash && hash.All(char.IsAsciiHexDigit);
        if (!isHash)
        {
            throw new ArgumentException($"The token hash for scope '{entry.ScopeName}' must be 64 hexadecimal characters.", nameof(entry));
        }
    }

    /// <summary>Warns at startup when a token lets its holder report as any scope, which is the weakest way to let agents in.</summary>
    private sealed class EnrollmentTokenWarning(SecurityOptions options, ILogger logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (options.AgentTokens.Any(entry => entry.AllowsAnyScope))
            {
                logger.LogWarning(
                    "An enrollment token (scope '*') is configured: anyone who holds it can report as any scope and replace what the dashboard shows for it. " +
                    "Use one token per scope, or service account or certificate authentication, where you can.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
