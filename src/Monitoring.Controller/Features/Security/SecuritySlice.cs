using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Security;

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
                new TokenAgentAuthenticator(options), new ServiceAccountAgentAuthenticator(new KubernetesTokenReviewer(client), accounts, provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Monitoring.Controller.Security"))));
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
}
