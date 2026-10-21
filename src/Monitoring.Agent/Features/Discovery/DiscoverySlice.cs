using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Agent.Features.Discovery.Kubernetes;

namespace Monitoring.Agent.Features.Discovery;

/// <summary>Registers the discovery slice.</summary>
public static class DiscoverySlice
{
    /// <summary>
    /// Adds the discovery of instances and the background service that keeps the scope state current.
    /// It writes to the scope state, so <c>AddScopeState</c> must be called as well.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="DiscoveryOptions"/>; with the default kind nothing is registered.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">Kubernetes is chosen but the namespace or API server cannot be determined, or an address is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An interval or delay is out of range.</exception>
    public static IServiceCollection AddDiscovery(this IServiceCollection services, Action<DiscoveryOptions> configure)
    {
        var options = new DiscoveryOptions();
        configure(options);

        switch (options.Kind)
        {
            case DiscoveryKind.None:
                return services;

            case DiscoveryKind.Kubernetes:
                AddKubernetes(services, options.Kubernetes);
                return services;

            default:
                throw new ArgumentOutOfRangeException(nameof(configure), options.Kind, "Unknown kind of discovery.");
        }
    }

    private static void AddKubernetes(IServiceCollection services, KubernetesDiscoveryOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ResyncInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RetryMinDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RetryMaxDelay, options.RetryMinDelay);

        var (target, connection) = KubernetesEnvironment.Resolve(options, Environment.GetEnvironmentVariable, ReadFileOrNull);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(target);
        services.AddSingleton<IKubernetesApi>(_ => new HttpKubernetesApi(KubernetesHttpClient.Create(connection)));
        services.AddHostedService<KubernetesDiscoveryService>();
    }

    private static string? ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
